namespace geoDC.UiDriver;

/// <summary>A node <c>find</c> matched, with the AutomationIds of its ancestors.</summary>
/// <param name="Node">The matched node.</param>
/// <param name="Path">
///     The AutomationIds of the node's ancestors from the search root down, skipping ancestors without one. Each is a
///     candidate <c>--scope</c> for the node.
/// </param>
public sealed record UiaTreeMatch(UiaNodeSnapshot Node, IReadOnlyList<string> Path)
{
    /// <summary>The node's <c>tree</c> line followed by <c>  path: A &gt; B</c>, or <c>path: —</c> at the root.</summary>
    public string FormatLine()
    {
        return $"{Node.FormatLine()}  path: {(Path.Count > 0 ? string.Join(" > ", Path) : "—")}";
    }
}

/// <summary>Reduces walked UIA snapshots to what a caller needs: the interactable outline, or the nodes it searched for.</summary>
public static class UiaTreeFilter
{
    /// <summary>
    ///     Keeps the root, every interactable node (see <see cref="UiaNodeSnapshot.InteractablePatterns" />), and each
    ///     interactable node's ancestors up to and including the nearest one with an AutomationId, so the scope that
    ///     disambiguates it stays visible. The intermediate ancestors without an AutomationId are kept too. Every other
    ///     node is dropped and its kept descendants move up to its nearest kept ancestor. A chain that meets no
    ///     AutomationId before the root ends at the root.
    /// </summary>
    public static UiaNodeSnapshot KeepInteractable(UiaNodeSnapshot root)
    {
        List<UiaNodeSnapshot> kept = [];
        foreach (UiaNodeSnapshot child in root.Children)
        {
            kept.AddRange(Prune(child).Kept);
        }

        return root with { Children = kept };
    }

    /// <summary>
    ///     Returns the nodes under <paramref name="roots" /> (roots included) that meet <paramref name="criteria" />, in
    ///     document order, each with its AutomationId breadcrumb. A node reached through more than one root (same
    ///     runtime id) is reported once, at its first occurrence.
    /// </summary>
    public static IReadOnlyList<UiaTreeMatch> Find(IEnumerable<UiaNodeSnapshot> roots, FindCriteria criteria)
    {
        List<UiaTreeMatch> matches = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> path = [];
        foreach (UiaNodeSnapshot root in roots)
        {
            Visit(root, criteria, path, matches, seen);
        }

        return matches;
    }

    /// <summary>
    ///     The kept forest for <paramref name="node" />'s subtree, and whether its top still needs an ancestor with an
    ///     AutomationId to complete a chain.
    /// </summary>
    private static (IReadOnlyList<UiaNodeSnapshot> Kept, bool NeedsAnchor) Prune(UiaNodeSnapshot node)
    {
        List<UiaNodeSnapshot> kept = [];
        bool descendantNeedsAnchor = false;
        foreach (UiaNodeSnapshot child in node.Children)
        {
            (IReadOnlyList<UiaNodeSnapshot> childKept, bool childNeedsAnchor) = Prune(child);
            kept.AddRange(childKept);
            descendantNeedsAnchor |= childNeedsAnchor;
        }

        if (!node.IsInteractable && !descendantNeedsAnchor)
        {
            return (kept, false);
        }

        // An interactable node always looks above itself for an anchor, even when it has its own AutomationId:
        // repeated template-part ids are disambiguated by an ancestor scope, not by the id itself.
        bool needsAnchor = node.IsInteractable || !node.HasAutomationId;
        return ([node with { Children = kept }], needsAnchor);
    }

    private static void Visit(UiaNodeSnapshot node, FindCriteria criteria, List<string> path,
        List<UiaTreeMatch> matches, HashSet<string> seen)
    {
        if (criteria.Matches(node) && (node.RuntimeId is not { Length: > 0 } runtimeId || seen.Add(runtimeId)))
        {
            matches.Add(new UiaTreeMatch(node, [.. path]));
        }

        bool pushed = false;
        if (node.AutomationId is { Length: > 0 } automationId)
        {
            path.Add(automationId);
            pushed = true;
        }

        foreach (UiaNodeSnapshot child in node.Children)
        {
            Visit(child, criteria, path, matches, seen);
        }

        if (pushed)
        {
            path.RemoveAt(path.Count - 1);
        }
    }
}