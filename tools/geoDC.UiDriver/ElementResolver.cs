using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver;

/// <summary>
///     Resolves elements by exact AutomationId across all geoDC top-level windows (or the main
///     window only), with a bounded retry loop. WPF pages load asynchronously, and retrying until
///     <c>--timeout</c> absorbs that, so a script does not need to add sleeps. An ambiguous match is
///     an error listing the candidates and the scope that selects each. A not-found error lists
///     near-matches, the windows holding the id outside a narrowed search, and a suggested next
///     step (see <see cref="ElementLookupAnalyzer" />).
/// </summary>
public sealed partial class ElementResolver(GeoDcSession session)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Layout settles in frames, not tree revisions, and probing one element is cheap.</summary>
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(75);

    /// <summary>
    ///     Finds the one element with AutomationId <paramref name="automationId" /> (exact, case-sensitive) under the
    ///     search roots of <see cref="SearchRoots" />, polling every 250 ms until it appears or
    ///     <paramref name="timeout" /> elapses.
    /// </summary>
    /// <param name="automationId">The exact AutomationId to find.</param>
    /// <param name="scopeId">The AutomationId of an element to search under, or null to search every window.</param>
    /// <param name="mainWindowOnly">True to search only the main window.</param>
    /// <param name="timeout">How long to keep polling for an element that is not there yet.</param>
    /// <exception cref="DriverException">
    ///     Exit code 2: <c>ambiguous-match</c> as soon as more than one element matches (with candidates and the scope
    ///     that selects each), <c>element-not-found</c> after the timeout (with near-matches and hints), or the
    ///     <c>scope-not-found</c> / <c>ambiguous-match</c> of an unresolvable scope.
    /// </exception>
    public AutomationElement Resolve(string automationId, string? scopeId, bool mainWindowOnly, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            AutomationElement[] roots = SearchRoots(scopeId, mainWindowOnly);
            List<AutomationElement> matches = FindById(roots, automationId);

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw new DriverException("ambiguous-match",
                    $"AutomationId '{automationId}' matches {matches.Count} elements; disambiguate with --scope.", 2,
                    [.. matches.Select(Describe)], null, DiagnoseAmbiguous(matches));
            }

            if (stopwatch.Elapsed >= timeout)
            {
                (IReadOnlyList<string> candidates, ElementLookupHints hints) =
                    DiagnoseNotFound(roots, automationId, "--id", scopeId, mainWindowOnly);
                throw new DriverException("element-not-found",
                    $"No element with AutomationId '{automationId}' within {timeout.TotalSeconds:0.#}s" +
                    (scopeId is null ? string.Empty : $" under scope '{scopeId}'") + ".", 2, candidates, null, hints);
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>
    ///     Resolves an element and then waits until it has been laid out: present in the tree, on
    ///     screen, and with a non-degenerate bounding rectangle.
    ///     <para>
    ///         Existence alone is a weak readiness signal. WPF adds an element to the automation tree
    ///         when it is created, which can be before measure and arrange. <c>IsOffscreen</c> and
    ///         <c>BoundingRectangle</c> are only meaningful afterwards, so requiring both means the
    ///         element occupies space on screen, which is what a script waiting to take a screenshot
    ///         needs. It does not check whether the data inside has finished arriving. Content that
    ///         animates or streams never settles, so a wait that required it to would hang.
    ///     </para>
    ///     <para>Both phases share one budget, so this cannot take materially longer than
    ///     <paramref name="timeout" />.</para>
    /// </summary>
    public (AutomationElement Element, bool WasImmediate) ResolveReady(string automationId, string? scopeId,
        bool mainWindowOnly, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AutomationElement element = Resolve(automationId, scopeId, mainWindowOnly, timeout);
        bool immediate = true;

        while (true)
        {
            bool? laidOut = TryReadLayout(element);
            if (laidOut == true)
            {
                return (element, immediate);
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new DriverException("element-not-ready",
                    $"Element '{automationId}' exists but was not laid out within {timeout.TotalSeconds:0.#}s " +
                    "(offscreen, or zero-sized).", 2, [Describe(element)], null, DiagnoseNotReady(element, automationId, scopeId));
            }

            immediate = false;
            Thread.Sleep(ReadyPollInterval);

            // A null read means the element went stale because WPF re-templated or re-created it. The handle
            // is dead, and only a fresh lookup can say whether the element came back.
            if (laidOut is null)
            {
                TimeSpan remaining = timeout - stopwatch.Elapsed;
                element = Resolve(automationId, scopeId, mainWindowOnly,
                    remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }
    }

    /// <summary>
    ///     Polls until the element identified by <paramref name="automationId" /> satisfies
    ///     <paramref name="conditions" />, returning its final observed state(s).
    ///     <para>
    ///         Unlike <see cref="Resolve" />, absence is an ordinary observation here rather than an error: it is the
    ///         goal of <c>--gone</c>, and for a state condition the element may simply not have appeared yet. A
    ///         missing <paramref name="scopeId" /> is treated the same way, since a closed popup takes its scope
    ///         anchor with it. Every poll re-searches the tree instead of trusting a held handle: a removed element's
    ///         handle is not guaranteed to fail fast, WPF can replace a control while the old handle stays readable,
    ///         and a second match appearing mid-wait must still fail as ambiguous.
    ///     </para>
    ///     On timeout throws <c>wait-timeout</c> carrying the last observation as <c>error.state</c>.
    /// </summary>
    public WaitResult WaitFor(string automationId, string? scopeId, WaitConditions conditions, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool firstLook = true;

        while (true)
        {
            List<AutomationElement> matches = [];
            bool scopeMissing = false;

            try
            {
                matches = FindById(SearchRoots(scopeId, false, diagnose: false), automationId);
            }
            catch (DriverException ex) when (ex.Code == "scope-not-found")
            {
                scopeMissing = true;
            }

            List<ObservedElementState> observed = [.. matches.Select(ElementObserver.Observe)];

            WaitOutcome outcome = conditions.Evaluate(observed);
            if (outcome == WaitOutcome.Met)
            {
                return new WaitResult(observed, firstLook, (int)stopwatch.ElapsedMilliseconds);
            }

            if (outcome == WaitOutcome.Ambiguous)
            {
                throw new DriverException("ambiguous-match",
                    $"AutomationId '{automationId}' matches {matches.Count} elements; disambiguate with --scope.", 2,
                    [.. matches.Select(Describe)], null, DiagnoseAmbiguous(matches));
            }

            if (stopwatch.Elapsed >= timeout)
            {
                string lastSeen = observed.Count switch
                {
                    0 => scopeMissing ? $"absent (scope '{scopeId}' not found)" : "absent",
                    1 => observed[0].Describe(),
                    _ => string.Join("; ", observed.Select(state => $"[{state.Describe()}]"))
                };
                object state = observed.Count switch
                {
                    0 => ObservedElementState.Absent,
                    1 => observed[0],
                    _ => observed
                };
                throw new DriverException("wait-timeout",
                    $"Element '{automationId}' did not become {string.Join(" + ", conditions.Labels)} within " +
                    $"{timeout.TotalSeconds:0.#}s" + (scopeId is null ? string.Empty : $" under scope '{scopeId}'") +
                    $". Last observed: {lastSeen}.", 2, null, state,
                    observed.Count == 0 ? DiagnoseAbsent(automationId, scopeId, scopeMissing) : null);
            }

            firstLook = false;
            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>True/false when the element could be read; null when it has gone stale.</summary>
    private static bool? TryReadLayout(AutomationElement element)
    {
        try
        {
            if (element.Properties.IsOffscreen.ValueOrDefault)
            {
                return false;
            }

            System.Drawing.Rectangle bounds = element.Properties.BoundingRectangle.ValueOrDefault;
            return bounds is { Width: > 0, Height: > 0 };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Resolves an actionable item inside a container by its UIA Name. Data-bound list and tree
    ///     items have no AutomationId (their Name comes from ToString overrides), so Name is the only
    ///     way to address them. Matches are tried in this order: exact (ordinal), exact
    ///     (case-insensitive), then a unique substring (case-insensitive). Actionable means
    ///     SelectionItem, Invoke or Toggle, because geoDC "lists" are often ItemsControls whose real
    ///     action is a button inside the item. When an item and its inner button share the name, the
    ///     one that supports SelectionItem wins. Virtualized items that were never realized are
    ///     invisible to UIA and cannot be matched.
    /// </summary>
    public (AutomationElement Element, string Match) ResolveItemByName(AutomationElement container,
        string containerId, string name, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (true)
        {
            List<(AutomationElement Element, string Name, bool Selectable)> items = [];
            foreach (AutomationElement candidate in SafeDescendants(container))
            {
                (bool actionable, bool selectable) = Actionable(candidate);
                if (actionable)
                {
                    items.Add((candidate, candidate.Properties.Name.ValueOrDefault ?? string.Empty, selectable));
                }
            }

            foreach (StringComparison comparison in
                     new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
            {
                if (Narrow(items, item => string.Equals(item.Name, name, comparison), containerId, name)
                    is { } exact)
                {
                    return (exact, "exact");
                }
            }

            if (Narrow(items, item => item.Name.Contains(name, StringComparison.OrdinalIgnoreCase), containerId,
                    name) is { } partial)
            {
                return (partial, "contains");
            }

            if (stopwatch.Elapsed >= timeout)
            {
                string[] available =
                    [.. items.Where(item => item.Name.Length > 0).Select(item => $"\"{item.Name}\"").Distinct()
                        .Take(20)];
                throw new DriverException("item-not-found",
                    $"No actionable item named '{name}' under '{containerId}' within {timeout.TotalSeconds:0.#}s. " +
                    "Items are matched by UIA Name; virtualized items scrolled far out of view may not be enumerable.",
                    2, available);
            }

            Thread.Sleep(PollInterval);
        }
    }

    private AutomationElement? Narrow(List<(AutomationElement Element, string Name, bool Selectable)> items,
        Func<(AutomationElement Element, string Name, bool Selectable), bool> predicate, string containerId,
        string name)
    {
        List<(AutomationElement Element, string Name, bool Selectable)> matches = [.. items.Where(predicate)];
        if (matches.Count > 1 && matches.Count(match => match.Selectable) == 1)
        {
            return matches.Single(match => match.Selectable).Element;
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0].Element,
            _ => throw new DriverException("ambiguous-match",
                $"Item name '{name}' matches {matches.Count} actionable items under '{containerId}'; " +
                "use a longer fragment or the full name.", 2,
                [.. matches.Select(match => $"\"{match.Name}\" — {Describe(match.Element)}")])
        };
    }

    private static (bool Actionable, bool Selectable) Actionable(AutomationElement element)
    {
        try
        {
            bool selectable = element.Patterns.SelectionItem.IsSupported;
            return (selectable || element.Patterns.Invoke.IsSupported || element.Patterns.Toggle.IsSupported,
                selectable);
        }
        catch (Exception)
        {
            return (false, false);
        }
    }
}