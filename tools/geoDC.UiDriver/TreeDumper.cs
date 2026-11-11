using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace geoDC.UiDriver;

/// <summary>
///     Walks the UIA control view into <see cref="UiaNodeSnapshot" /> trees, either in full or as the interactable
///     outline, and renders them as an indented, greppable text dump. Each element is read once, so a node that
///     changes mid-walk is printed as it was when reached.
/// </summary>
public sealed class TreeDumper(GeoDcSession session)
{
    /// <summary>The number of elements read from UIA since this dumper was created.</summary>
    public int WalkedCount { get; private set; }

    /// <summary>
    ///     Reads each window's subtree to <see cref="TreeOptions.Depth" />, reduced to the interactable outline when
    ///     <see cref="TreeOptions.Interactable" /> is set: the roots <see cref="Render" /> prints.
    /// </summary>
    public IReadOnlyList<UiaNodeSnapshot> ReadWindows(IReadOnlyList<AutomationElement> windows, TreeOptions options)
    {
        return [.. windows.Select(window => Read(window, options))];
    }

    /// <summary>Reads one element's subtree as <see cref="ReadWindows" /> reads a window's.</summary>
    public UiaNodeSnapshot Read(AutomationElement root, TreeOptions options)
    {
        UiaNodeSnapshot snapshot = Snapshot(root, 0, options.Depth);
        return options.Interactable ? UiaTreeFilter.KeepInteractable(snapshot) : snapshot;
    }

    /// <summary>One <c>Window: "title" handle=0x… class=…</c> line per window, as a whole-window dump starts.</summary>
    public static string WindowHeaders(IReadOnlyList<AutomationElement> windows)
    {
        StringBuilder builder = new();
        foreach (AutomationElement window in windows)
        {
            string title = window.Properties.Name.ValueOrDefault ?? string.Empty;
            IntPtr handle = window.Properties.NativeWindowHandle.ValueOrDefault;
            string className = window.Properties.ClassName.ValueOrDefault ?? string.Empty;
            builder.AppendLine($"Window: \"{title}\" handle=0x{handle:X8} class={className}");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Prints <paramref name="roots" /> one indented line per node, with a blank line before each root when
    ///     <paramref name="separateRoots" /> is set.
    /// </summary>
    public static string Render(IReadOnlyList<UiaNodeSnapshot> roots, bool separateRoots)
    {
        StringBuilder builder = new();
        foreach (UiaNodeSnapshot root in roots)
        {
            if (separateRoots)
            {
                builder.AppendLine();
            }

            root.WriteTree(builder);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Walks each root's whole subtree and returns the nodes matching <paramref name="criteria" />; see
    ///     <see cref="UiaTreeFilter.Find" />.
    /// </summary>
    public IReadOnlyList<UiaTreeMatch> Find(IReadOnlyList<AutomationElement> roots, FindCriteria criteria)
    {
        return UiaTreeFilter.Find(ReadSubtrees(roots), criteria);
    }

    /// <summary>Reads each root's whole subtree with no depth limit and no filtering, as <c>find</c> and <c>audit</c> walk it.</summary>
    public IReadOnlyList<UiaNodeSnapshot> ReadSubtrees(IReadOnlyList<AutomationElement> roots)
    {
        return [.. roots.Select(root => Snapshot(root, 0, null))];
    }

    private UiaNodeSnapshot Snapshot(AutomationElement element, int level, int? maxDepth)
    {
        WalkedCount++;
        IReadOnlyList<string> patterns = ReadPatterns(element);
        List<UiaNodeSnapshot> children = [];
        if (maxDepth is null || level < maxDepth)
        {
            ITreeWalker walker = session.Automation.TreeWalkerFactory.GetControlViewWalker();
            AutomationElement? child = SafeStep(() => walker.GetFirstChild(element));
            while (child is not null)
            {
                children.Add(Snapshot(child, level + 1, maxDepth));
                AutomationElement current = child;
                child = SafeStep(() => walker.GetNextSibling(current));
            }
        }

        int[] runtimeId = Safe(() => element.Properties.RuntimeId.ValueOrDefault, null) ?? [];
        return new UiaNodeSnapshot(
            Safe(() => element.Properties.ControlType.ValueOrDefault.ToString(), "Unknown"),
            Safe(() => element.Properties.AutomationId.ValueOrDefault, null) is { Length: > 0 } id ? id : null,
            Safe(() => element.Properties.Name.ValueOrDefault, null) ?? string.Empty,
            Safe(() => element.Properties.BoundingRectangle.ValueOrDefault, System.Drawing.Rectangle.Empty),
            Safe(() => element.Properties.IsEnabled.ValueOrDefault, false),
            Safe(() => element.Properties.IsOffscreen.ValueOrDefault, false),
            ReadState(element, patterns),
            patterns,
            children,
            runtimeId.Length > 0 ? string.Join(",", runtimeId) : null);
    }

    /// <summary>
    ///     Current state of the stateful supported patterns, so "did settext take?" / "which tab is selected?" are
    ///     answerable from the dump without a screenshot.
    /// </summary>
    private static UiaNodeState ReadState(AutomationElement element, IReadOnlyList<string> patterns)
    {
        string? value = patterns.Contains("Value")
            ? Safe(() => element.Patterns.Value.Pattern.Value.ValueOrDefault, null) ?? string.Empty
            : null;

        string? toggled = patterns.Contains("Toggle")
            ? Safe(() => element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault, ToggleState.Indeterminate) switch
            {
                ToggleState.On => "on",
                ToggleState.Off => "off",
                _ => "mixed"
            }
            : null;

        bool selected = patterns.Contains("SelectionItem") &&
                        Safe(() => element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, false);

        ExpandCollapseState expansion = patterns.Contains("ExpandCollapse")
            ? Safe(() => element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault,
                ExpandCollapseState.LeafNode)
            : ExpandCollapseState.LeafNode;
        string? expanded = expansion switch
        {
            ExpandCollapseState.Expanded => "expanded",
            ExpandCollapseState.PartiallyExpanded => "partial",
            ExpandCollapseState.Collapsed => "collapsed",
            _ => null
        };

        string? scroll = null;
        if (patterns.Contains("Scroll"))
        {
            double horizontal = Safe(() => element.Patterns.Scroll.Pattern.HorizontalScrollPercent.ValueOrDefault,
                -1d);
            double vertical = Safe(() => element.Patterns.Scroll.Pattern.VerticalScrollPercent.ValueOrDefault, -1d);
            scroll = $"h={FormatPercent(horizontal)} v={FormatPercent(vertical)}";
        }

        return new UiaNodeState(value, toggled, selected, expanded, scroll);
    }

    private static string FormatPercent(double value)
    {
        return value < 0 ? "n/a" : $"{value:0.#}%";
    }

    /// <summary>The supported patterns, named and ordered as in <see cref="UiaNodeSnapshot.PatternNames" />.</summary>
    private static IReadOnlyList<string> ReadPatterns(AutomationElement element)
    {
        List<string> supported = [];
        AddIf(supported, "Invoke", () => element.Patterns.Invoke.IsSupported);
        AddIf(supported, "Value", () => element.Patterns.Value.IsSupported);
        AddIf(supported, "Toggle", () => element.Patterns.Toggle.IsSupported);
        AddIf(supported, "Selection", () => element.Patterns.Selection.IsSupported);
        AddIf(supported, "SelectionItem", () => element.Patterns.SelectionItem.IsSupported);
        AddIf(supported, "ExpandCollapse", () => element.Patterns.ExpandCollapse.IsSupported);
        AddIf(supported, "Scroll", () => element.Patterns.Scroll.IsSupported);
        AddIf(supported, "ScrollItem", () => element.Patterns.ScrollItem.IsSupported);
        return supported;
    }

    private static void AddIf(List<string> patterns, string name, Func<bool> isSupported)
    {
        if (Safe(isSupported, false))
        {
            patterns.Add(name);
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static AutomationElement? SafeStep(Func<AutomationElement?> step)
    {
        try
        {
            return step();
        }
        catch (Exception)
        {
            return null;
        }
    }
}