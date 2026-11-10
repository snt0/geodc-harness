using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace geoDC.UiDriver;

// What a failed lookup reports: candidates, the windows holding the id elsewhere, and the suggested next step.
public sealed partial class ElementResolver
{
    /// <summary>
    ///     Reads what a not-found error reports from the searched <paramref name="roots" />: the closest
    ///     AutomationIds (as <c>candidates</c>, described, and as bare ids in the hints), the windows that hold the
    ///     id outside a narrowed search, the scroll containers a virtualized item could hide in, and the hint.
    /// </summary>
    private (IReadOnlyList<string> Candidates, ElementLookupHints Hints) DiagnoseNotFound(
        IReadOnlyList<AutomationElement> roots, string requestedId, string option, string? scopeId,
        bool mainWindowOnly)
    {
        Dictionary<string, AutomationElement> firstById = new(StringComparer.Ordinal);
        List<string> listScrollers = [];
        List<string> paneScrollers = [];

        foreach (AutomationElement root in roots)
        {
            // FindById considers each root as well as its descendants, so a typo of a root's own id is a candidate.
            foreach (AutomationElement element in SafeDescendants(root).Prepend(root))
            {
                string id = SafeAutomationId(element);
                if (id.Length > 0 && firstById.TryAdd(id, element))
                {
                    CollectScrollContainer(element, id, listScrollers, paneScrollers);
                }
            }
        }

        IReadOnlyList<string> elsewhere = scopeId is not null || mainWindowOnly
            ? FindElsewhere(requestedId, excludeMain: scopeId is null)
            : [];
        ElementNotFoundFacts facts = new(requestedId, option, scopeId, mainWindowOnly, firstById.Keys, elsewhere,
            [.. listScrollers, .. paneScrollers]);
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(facts);
        return ([.. hints.Closest.Select(id => Describe(firstById[id]))], hints);
    }

    /// <summary>
    ///     Hints for a wait or assert whose element was absent throughout: the not-found diagnosis of the element,
    ///     or of the scope when the scope itself was missing. Null when the tree changed under the diagnosis.
    /// </summary>
    private ElementLookupHints? DiagnoseAbsent(string automationId, string? scopeId, bool scopeMissing)
    {
        try
        {
            if (scopeMissing && scopeId is not null)
            {
                return DiagnoseNotFound(session.TopLevelWindows(), scopeId, "--scope", null, false).Hints;
            }

            return DiagnoseNotFound(SearchRoots(scopeId, false, diagnose: false), automationId, "--id", scopeId, false)
                .Hints;
        }
        catch (DriverException)
        {
            // The scope appeared, vanished or multiplied since the last poll. The timeout is still the failure to
            // report, just without hints.
            return null;
        }
    }

    /// <summary>
    ///     Each ambiguous match with the ancestor that scopes to it alone. Uniqueness is checked across every
    ///     window, because <c>--scope</c> resolves across every window.
    /// </summary>
    private ElementLookupHints DiagnoseAmbiguous(IReadOnlyList<AutomationElement> matches)
    {
        AutomationElement[] windows = session.TopLevelWindows();
        Dictionary<string, bool> uniqueById = new(StringComparer.Ordinal);

        bool IsUniqueId(string id)
        {
            if (!uniqueById.TryGetValue(id, out bool unique))
            {
                unique = FindById(windows, id).Count == 1;
                uniqueById[id] = unique;
            }

            return unique;
        }

        // The full ancestry, not the six-id chain Describe prints: a shared ancestor beyond one match's sixth id
        // would otherwise look unique to the other match.
        List<MatchAncestry> ancestry =
            [.. matches.Select(match => new MatchAncestry(Describe(match), AncestorIds(match, int.MaxValue)))];
        return ElementLookupAnalyzer.Ambiguous(ancestry, IsUniqueId);
    }

    private ElementLookupHints DiagnoseNotReady(AutomationElement element, string automationId, string? scopeId)
    {
        bool offscreen;
        try
        {
            offscreen = element.Properties.IsOffscreen.ValueOrDefault;
        }
        catch (Exception)
        {
            // A stale element reads as zero-sized, which is what the not-ready message already says.
            offscreen = false;
        }

        AutomationElement? scroller = offscreen ? NearestScrollableAncestor(element) : null;
        string? scrollerId = scroller is null ? null : SafeAutomationId(scroller);
        return ElementLookupHints.Empty with
        {
            Hint = ElementLookupAnalyzer.NotReadyHint(automationId, offscreen, scroller is not null,
                string.IsNullOrEmpty(scrollerId) ? null : scrollerId, scopeId)
        };
    }

    /// <summary>
    ///     The <c>--window</c> selectors of the windows that hold <paramref name="automationId" />, searching every
    ///     window. Each match is attributed to its nearest window ancestor, since an owned popup also appears under
    ///     its owner in UIA. <paramref name="excludeMain" /> drops the main window, for a <c>--main-window</c> search
    ///     that already looked there.
    /// </summary>
    private IReadOnlyList<string> FindElsewhere(string automationId, bool excludeMain)
    {
        AutomationElement[] windows = session.TopLevelWindows();
        List<AutomationElement> matches = FindById(windows, automationId);
        if (matches.Count == 0)
        {
            return [];
        }

        IntPtr mainHandle = SafeHandle(session.MainWindow());
        string[] titles = [.. windows.Select(SafeName)];
        List<string> selectors = [];

        foreach (AutomationElement match in matches)
        {
            AutomationElement window = TopLevelAncestor(match);
            IntPtr handle = SafeHandle(window);
            bool isMain = handle == mainHandle;
            string selector = ElementLookupAnalyzer.WindowSelector(SafeName(window), handle.ToInt64(), isMain, titles);
            if (!(excludeMain && isMain) && !selectors.Contains(selector))
            {
                selectors.Add(selector);
            }
        }

        return selectors;
    }

    /// <summary>
    ///     Adds <paramref name="id" /> to <paramref name="lists" /> (List, DataGrid, Tree, Table) or
    ///     <paramref name="panes" /> (a ScrollViewer appears as a Pane) when the element can scroll, keeping a few
    ///     of each. These are the containers whose virtualized items exist only once scrolled into view.
    /// </summary>
    private static void CollectScrollContainer(AutomationElement element, string id, List<string> lists,
        List<string> panes)
    {
        const int Keep = 3;
        try
        {
            FlaUI.Core.Definitions.ControlType type = element.Properties.ControlType.ValueOrDefault;
            bool listLike = type is FlaUI.Core.Definitions.ControlType.List
                or FlaUI.Core.Definitions.ControlType.DataGrid
                or FlaUI.Core.Definitions.ControlType.Tree
                or FlaUI.Core.Definitions.ControlType.Table;
            List<string> target = listLike ? lists : panes;
            if ((!listLike && type != FlaUI.Core.Definitions.ControlType.Pane) || target.Count >= Keep ||
                !element.Patterns.Scroll.IsSupported)
            {
                return;
            }

            IScrollPattern pattern = element.Patterns.Scroll.Pattern;
            if (pattern.VerticallyScrollable.ValueOrDefault || pattern.HorizontallyScrollable.ValueOrDefault)
            {
                target.Add(id);
            }
        }
        catch (Exception)
        {
            // Reading a pattern can race a disappearing element, which is then simply not a candidate.
        }
    }
}