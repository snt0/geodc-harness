using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace geoDC.UiDriver;

// Search roots, ancestor walks and the exception-safe property reads the rest of the resolver builds on.
public sealed partial class ElementResolver
{
    /// <summary>
    ///     The roots an AutomationId is searched under: the <paramref name="scopeId" /> element, or else every
    ///     top-level window (the main window only under <paramref name="mainWindowOnly" />). Throws
    ///     <c>scope-not-found</c> or <c>ambiguous-match</c> when the scope does not resolve to one element.
    /// </summary>
    /// <param name="scopeId">The exact AutomationId of the element to search under, or null to search the windows.</param>
    /// <param name="mainWindowOnly">
    ///     True to search only the main window, both for the scope element and, without a scope, as the only root.
    /// </param>
    /// <param name="diagnose">
    ///     False for a caller that polls and treats a missing scope as an observation: the scope-not-found error
    ///     then carries no candidates or hints, which would cost a sweep of every window on each poll.
    /// </param>
    public AutomationElement[] SearchRoots(string? scopeId, bool mainWindowOnly, bool diagnose = true)
    {
        AutomationElement[] windows = mainWindowOnly ? [session.MainWindow()] : session.TopLevelWindows();
        if (scopeId is null)
        {
            return windows;
        }

        List<AutomationElement> scopes = FindById(windows, scopeId);
        if (scopes.Count == 1)
        {
            return [scopes[0]];
        }

        if (scopes.Count == 0)
        {
            (IReadOnlyList<string> candidates, ElementLookupHints? hints) = diagnose
                ? DiagnoseNotFound(windows, scopeId, "--scope", null, mainWindowOnly)
                : ([], null);
            throw new DriverException("scope-not-found", $"No element with AutomationId '{scopeId}' to scope to.", 2,
                candidates, null, hints);
        }

        throw new DriverException("ambiguous-match", $"Scope AutomationId '{scopeId}' matches {scopes.Count} elements.",
            2, [.. scopes.Select(Describe)], null, DiagnoseAmbiguous(scopes));
    }

    /// <summary>Ancestor chain of AutomationIds, innermost last, such as "SurveyPage &gt; CommandBar".</summary>
    public string AncestorChain(AutomationElement element)
    {
        List<string> parts = AncestorIds(element, 6);
        parts.Reverse();
        return string.Join(" > ", parts);
    }

    /// <summary>
    ///     The non-empty AutomationIds of the element's ancestors within its process, innermost first, stopping
    ///     after <paramref name="limit" /> ids.
    /// </summary>
    private List<string> AncestorIds(AutomationElement element, int limit)
    {
        List<string> ids = [];
        FlaUI.Core.ITreeWalker walker = session.Automation.TreeWalkerFactory.GetControlViewWalker();
        AutomationElement? current = SafeParent(walker, element);

        while (current is not null && ids.Count < limit)
        {
            string id = SafeAutomationId(current);
            if (id.Length > 0)
            {
                ids.Add(id);
            }

            current = SafeParent(walker, current);
        }

        return ids;
    }

    /// <summary>
    ///     The window containing the element: the nearest Window ancestor, not the outermost one. An owned
    ///     window (every detached or immersive popup) is parented under its owner in UIA, so walking to the
    ///     process root would name the main window and a capture would render main-window pixels where the
    ///     popup was asked for.
    /// </summary>
    public AutomationElement TopLevelAncestor(AutomationElement element)
    {
        FlaUI.Core.ITreeWalker walker = session.Automation.TreeWalkerFactory.GetControlViewWalker();
        AutomationElement current = element;
        AutomationElement outermost = element;

        while (true)
        {
            if (IsWindow(current))
            {
                return current;
            }

            if (SafeParent(walker, current) is not { } parent)
            {
                return outermost;
            }

            current = parent;
            outermost = parent;
        }
    }

    /// <summary>
    ///     The nearest ancestor exposing a usable ScrollPattern, or null when the element sits in no
    ///     scroll container. WPF grants ScrollItemPattern only to generated item containers, so a plain
    ///     layout panel can only be revealed by scrolling the viewport that owns it.
    /// </summary>
    public AutomationElement? NearestScrollableAncestor(AutomationElement element)
    {
        FlaUI.Core.ITreeWalker walker = session.Automation.TreeWalkerFactory.GetControlViewWalker();
        AutomationElement? current = SafeParent(walker, element);

        while (current is not null)
        {
            if (IsWindow(current))
            {
                return null;
            }

            try
            {
                if (current.Patterns.Scroll.IsSupported)
                {
                    IScrollPattern pattern = current.Patterns.Scroll.Pattern;
                    if (pattern.VerticallyScrollable.ValueOrDefault ||
                        pattern.HorizontallyScrollable.ValueOrDefault)
                    {
                        return current;
                    }
                }
            }
            catch (Exception)
            {
                // Reading a pattern can race a disappearing element; keep walking.
            }

            current = SafeParent(walker, current);
        }

        return null;
    }

    private static bool IsWindow(AutomationElement element)
    {
        try
        {
            return element.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Window &&
                   element.Properties.NativeWindowHandle.ValueOrDefault != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     A one-line label for <paramref name="element" /> in an error's candidate list:
    ///     <c>id (ControlType, ancestor &gt; chain)</c>, with <c>—</c> for a missing AutomationId and the chain left out
    ///     when no ancestor has an id. Reads UIA properties of the live element.
    /// </summary>
    public string Describe(AutomationElement element)
    {
        string id = element.Properties.AutomationId.ValueOrDefault ?? "—";
        string controlType = SafeControlType(element);
        string chain = AncestorChain(element);
        return chain.Length > 0 ? $"{id} ({controlType}, {chain})" : $"{id} ({controlType})";
    }

    private static List<AutomationElement> FindById(IReadOnlyList<AutomationElement> roots, string automationId)
    {
        // Dedupe by UIA runtime id: during startup/layout the desktop can briefly enumerate the
        // same window twice, which would turn a unique element into a phantom ambiguous match.
        List<AutomationElement> matches = [];
        HashSet<string> seen = [];
        foreach (AutomationElement root in roots)
        {
            if (string.Equals(root.Properties.AutomationId.ValueOrDefault, automationId, StringComparison.Ordinal) &&
                seen.Add(RuntimeIdKey(root)))
            {
                matches.Add(root);
            }

            foreach (AutomationElement match in root.FindAllDescendants(cf => cf.ByAutomationId(automationId)))
            {
                if (seen.Add(RuntimeIdKey(match)))
                {
                    matches.Add(match);
                }
            }
        }

        return matches;
    }

    private static string RuntimeIdKey(AutomationElement element)
    {
        try
        {
            int[] runtimeId = element.Properties.RuntimeId.ValueOrDefault ?? [];
            return runtimeId.Length > 0 ? string.Join(",", runtimeId) : Guid.NewGuid().ToString();
        }
        catch (Exception)
        {
            return Guid.NewGuid().ToString();
        }
    }


    private static string SafeAutomationId(AutomationElement element)
    {
        try
        {
            return element.Properties.AutomationId.ValueOrDefault ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Properties.Name.ValueOrDefault ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static IntPtr SafeHandle(AutomationElement element)
    {
        try
        {
            return element.Properties.NativeWindowHandle.ValueOrDefault;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static AutomationElement[] SafeDescendants(AutomationElement root)
    {
        try
        {
            return root.FindAllDescendants();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static AutomationElement? SafeParent(FlaUI.Core.ITreeWalker walker, AutomationElement element)
    {
        try
        {
            AutomationElement? parent = walker.GetParent(element);
            // Stop at the desktop root (its parent is null / itself has no AutomationId anyway).
            return parent is null || parent.Properties.ProcessId.ValueOrDefault != element.Properties.ProcessId.ValueOrDefault
                ? null
                : parent;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeControlType(AutomationElement element)
    {
        try
        {
            return element.Properties.ControlType.ValueOrDefault.ToString();
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }
}