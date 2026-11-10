using System.Text;
using System.Text.Json.Serialization;

namespace geoDC.UiDriver;

/// <summary>One field of a node that differs between two snapshots.</summary>
/// <param name="From">The previous value: a string, a bool, or null when the pattern was not supported.</param>
/// <param name="To">The current value, typed as <paramref name="From" />.</param>
public sealed record UiaFieldChange(object? From, object? To);

/// <summary>
///     The root of a subtree that one snapshot has and the other lacks. Only the root is listed. Its descendants are
///     counted in <paramref name="Descendants" /> and belong to the same addition or removal.
/// </summary>
/// <param name="Role">The root's control type.</param>
/// <param name="Id">The root's AutomationId, or null when it has none.</param>
/// <param name="Name">The root's UIA Name.</param>
/// <param name="Path">The AutomationIds of the root's ancestors from the snapshot root down, skipping those without one.</param>
/// <param name="Descendants">The number of nodes below the root.</param>
/// <param name="Node">The subtree itself, for the text rendering; not serialized.</param>
public sealed record UiaDiffSubtree(
    string Role,
    string? Id,
    string Name,
    IReadOnlyList<string> Path,
    int Descendants,
    [property: JsonIgnore] UiaNodeSnapshot Node)
{
    /// <summary>
    ///     The entry for the subtree rooted at <paramref name="node" />, with <paramref name="path" /> copied and its
    ///     descendants counted at every level.
    /// </summary>
    /// <param name="node">The root of the added or removed subtree.</param>
    /// <param name="path">The AutomationIds of the node's ancestors from the snapshot root down.</param>
    public static UiaDiffSubtree Of(UiaNodeSnapshot node, IReadOnlyList<string> path)
    {
        return new UiaDiffSubtree(node.ControlType, node.AutomationId, node.Name, [.. path], node.CountDescendants(),
            node);
    }
}

/// <summary>A node present in both snapshots whose compared fields differ.</summary>
/// <param name="Role">The node's control type.</param>
/// <param name="Id">The node's AutomationId, or null when it has none.</param>
/// <param name="Name">The node's current UIA Name.</param>
/// <param name="Path">The AutomationIds of the node's ancestors from the snapshot root down, skipping those without one.</param>
/// <param name="Changes">
///     Only the fields that differ, keyed by the names in <see cref="UiaTreeDiff.Fields" /> and in that order.
/// </param>
public sealed record UiaDiffChange(
    string Role,
    string? Id,
    string Name,
    IReadOnlyList<string> Path,
    IReadOnlyDictionary<string, UiaFieldChange> Changes);

/// <summary>
///     What changed between two readings of the same <c>tree</c>: the subtrees added and removed, and the nodes whose
///     fields changed.
///     <para>
///         Nodes are matched level by level, among the children of a matched parent (or among the roots). A child with
///         an AutomationId matches the child of the other snapshot with the same AutomationId. A child without one
///         matches by control type and Name. When several siblings share an AutomationId, or a control type and Name,
///         they match in order of occurrence. Children without an AutomationId that are still unmatched then pair up
///         by control type, in order, when both snapshots have the same number of them left of that type. This step
///         reports a text block whose Name changed as a <c>name</c> change instead of a removal and an addition.
///     </para>
///     <para>
///         An unmatched node is reported once, as the root of an added or removed subtree, and its descendants are
///         not listed. A node whose parent did not match is not reported on its own, so a node that moves to another
///         parent appears inside a removed and an added subtree.
///     </para>
/// </summary>
/// <param name="Added">The roots of subtrees only the current snapshot has, in document order.</param>
/// <param name="Removed">The roots of subtrees only the previous snapshot had, in document order.</param>
/// <param name="Changed">The matched nodes with changed fields, in the current snapshot's document order.</param>
public sealed record UiaTreeDiff(
    IReadOnlyList<UiaDiffSubtree> Added,
    IReadOnlyList<UiaDiffSubtree> Removed,
    IReadOnlyList<UiaDiffChange> Changed)
{
    /// <summary>
    ///     The compared fields, in the order a change lists them. <c>role</c> can differ only for nodes matched by
    ///     AutomationId, since the other matches already require the same control type. <c>bounds</c> is compared only
    ///     when asked for, since layout shifts would otherwise bury the state changes. Patterns, scroll position and
    ///     runtime id are not compared.
    /// </summary>
    public static readonly IReadOnlyList<string> Fields =
        ["role", "name", "value", "enabled", "toggled", "selected", "expanded", "offscreen", "bounds"];

    /// <summary>True when nothing was added, removed or changed; not serialized.</summary>
    [JsonIgnore]
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>
    ///     Compares two readings of the same roots. <paramref name="includeBounds" /> adds bounding-rectangle changes,
    ///     reported as <c>x,y wxh</c> strings.
    /// </summary>
    public static UiaTreeDiff Compare(IReadOnlyList<UiaNodeSnapshot> previous, IReadOnlyList<UiaNodeSnapshot> current,
        bool includeBounds)
    {
        List<UiaDiffSubtree> added = [];
        List<UiaDiffSubtree> removed = [];
        List<UiaDiffChange> changed = [];
        CompareSiblings(previous, current, [], includeBounds, added, removed, changed);
        return new UiaTreeDiff(added, removed, changed);
    }

    /// <summary>
    ///     The diff as text, or empty when nothing changed:
    ///     <list type="bullet">
    ///         <item>each added subtree in full, one <c>tree</c> line per node, prefixed <c>+ </c>;</item>
    ///         <item>each removed subtree's root, prefixed <c>- </c>, with its descendant count;</item>
    ///         <item>
    ///             each changed node, prefixed <c>~ </c>, with its changes as <c>field: from -&gt; to</c> and name and
    ///             value quoted.
    ///         </item>
    ///     </list>
    ///     The first line of each entry ends with its <c>path:</c>.
    /// </summary>
    public string Format()
    {
        StringBuilder builder = new();
        foreach (UiaDiffSubtree subtree in Added)
        {
            StringBuilder lines = new();
            subtree.Node.WriteTree(lines);
            string[] split = lines.ToString().TrimEnd().Split(Environment.NewLine);
            builder.AppendLine($"+ {split[0]}  path: {FormatPath(subtree.Path)}");
            foreach (string line in split.Skip(1))
            {
                builder.AppendLine($"+ {line}");
            }
        }

        foreach (UiaDiffSubtree subtree in Removed)
        {
            builder.Append($"- {subtree.Node.FormatLine()}  path: {FormatPath(subtree.Path)}");
            builder.AppendLine(subtree.Descendants > 0 ? $"  descendants: {subtree.Descendants}" : string.Empty);
        }

        foreach (UiaDiffChange change in Changed)
        {
            builder.Append($"~ [{change.Role}] id: {change.Id ?? "—"}");
            if (change.Name.Length > 0)
            {
                builder.Append($"  name: \"{UiaNodeState.Printable(change.Name, UiaNodeState.PrintedValueLength)}\"");
            }

            builder.Append($"  path: {FormatPath(change.Path)}");
            foreach ((string field, UiaFieldChange value) in change.Changes)
            {
                builder.Append($"  {field}: {FormatValue(field, value.From)} -> {FormatValue(field, value.To)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void CompareSiblings(IReadOnlyList<UiaNodeSnapshot> previous,
        IReadOnlyList<UiaNodeSnapshot> current, List<string> path, bool includeBounds, List<UiaDiffSubtree> added,
        List<UiaDiffSubtree> removed, List<UiaDiffChange> changed)
    {
        int[] partners = Pair(previous, current);
        HashSet<int> matchedPrevious = [.. partners.Where(partner => partner >= 0)];
        for (int index = 0; index < previous.Count; index++)
        {
            if (!matchedPrevious.Contains(index))
            {
                removed.Add(UiaDiffSubtree.Of(previous[index], path));
            }
        }

        for (int index = 0; index < current.Count; index++)
        {
            UiaNodeSnapshot node = current[index];
            if (partners[index] < 0)
            {
                added.Add(UiaDiffSubtree.Of(node, path));
                continue;
            }

            UiaNodeSnapshot before = previous[partners[index]];
            IReadOnlyDictionary<string, UiaFieldChange> changes = Changes(before, node, includeBounds);
            if (changes.Count > 0)
            {
                changed.Add(new UiaDiffChange(node.ControlType, node.AutomationId, node.Name, [.. path], changes));
            }

            bool pushed = false;
            if (node.AutomationId is { Length: > 0 } automationId)
            {
                path.Add(automationId);
                pushed = true;
            }

            CompareSiblings(before.Children, node.Children, path, includeBounds, added, removed, changed);
            if (pushed)
            {
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>For each current sibling, the index of its previous partner, or -1 when it has none.</summary>
    private static int[] Pair(IReadOnlyList<UiaNodeSnapshot> previous, IReadOnlyList<UiaNodeSnapshot> current)
    {
        int[] partners = Enumerable.Repeat(-1, current.Count).ToArray();
        bool[] taken = new bool[previous.Count];

        Dictionary<string, int> previousByKey = new(StringComparer.Ordinal);
        foreach ((string key, int index) in OccurrenceKeys(previous))
        {
            previousByKey[key] = index;
        }

        foreach ((string key, int index) in OccurrenceKeys(current))
        {
            if (previousByKey.TryGetValue(key, out int partner))
            {
                partners[index] = partner;
                taken[partner] = true;
            }
        }

        IEnumerable<IGrouping<string, int>> leftoverCurrent = Enumerable.Range(0, current.Count)
            .Where(index => partners[index] < 0 && !current[index].HasAutomationId)
            .GroupBy(index => current[index].ControlType, StringComparer.Ordinal);
        foreach (IGrouping<string, int> group in leftoverCurrent)
        {
            int[] currentLeft = [.. group];
            int[] previousLeft =
            [
                .. Enumerable.Range(0, previous.Count).Where(index =>
                    !taken[index] && !previous[index].HasAutomationId &&
                    string.Equals(previous[index].ControlType, group.Key, StringComparison.Ordinal))
            ];
            if (previousLeft.Length != currentLeft.Length)
            {
                continue;
            }

            for (int i = 0; i < currentLeft.Length; i++)
            {
                partners[currentLeft[i]] = previousLeft[i];
                taken[previousLeft[i]] = true;
            }
        }

        return partners;
    }

    /// <summary>
    ///     Each sibling's matching key: its AutomationId, or its control type and Name when it has none, followed by
    ///     how many earlier siblings share that identity.
    /// </summary>
    private static IEnumerable<(string Key, int Index)> OccurrenceKeys(IReadOnlyList<UiaNodeSnapshot> siblings)
    {
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        for (int index = 0; index < siblings.Count; index++)
        {
            UiaNodeSnapshot node = siblings[index];
            string identity = node.HasAutomationId
                ? $"id\u001f{node.AutomationId}"
                : $"anonymous\u001f{node.ControlType}\u001f{node.Name}";
            int occurrence = occurrences.GetValueOrDefault(identity);
            occurrences[identity] = occurrence + 1;
            yield return ($"{identity}\u001f{occurrence}", index);
        }
    }

    private static IReadOnlyDictionary<string, UiaFieldChange> Changes(UiaNodeSnapshot before, UiaNodeSnapshot after,
        bool includeBounds)
    {
        OrderedDictionary<string, UiaFieldChange> changes = new(StringComparer.Ordinal);
        AddIfChanged(changes, "role", before.ControlType, after.ControlType);
        AddIfChanged(changes, "name", before.Name, after.Name);
        AddIfChanged(changes, "value", before.State.Value, after.State.Value);
        AddIfChanged(changes, "enabled", before.IsEnabled, after.IsEnabled);
        AddIfChanged(changes, "toggled", before.State.Toggled, after.State.Toggled);
        AddIfChanged(changes, "selected", before.State.Selected, after.State.Selected);
        AddIfChanged(changes, "expanded", before.State.Expanded, after.State.Expanded);
        AddIfChanged(changes, "offscreen", before.IsOffscreen, after.IsOffscreen);
        if (includeBounds)
        {
            AddIfChanged(changes, "bounds", UiaNodeSnapshot.FormatBounds(before.Bounds),
                UiaNodeSnapshot.FormatBounds(after.Bounds));
        }

        return changes;
    }

    private static void AddIfChanged<T>(OrderedDictionary<string, UiaFieldChange> changes, string field, T before,
        T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            changes[field] = new UiaFieldChange(before, after);
        }
    }

    private static string FormatPath(IReadOnlyList<string> path)
    {
        return path.Count > 0 ? string.Join(" > ", path) : "—";
    }

    /// <summary>A changed field's value as text: name and value quoted and escaped, other fields bare, null as <c>—</c>.</summary>
    private static string FormatValue(string field, object? value)
    {
        return value switch
        {
            null => "—",
            string text when field is "name" or "value" =>
                $"\"{UiaNodeState.Printable(text, UiaNodeState.PrintedValueLength)}\"",
            string text => text,
            bool flag => flag ? "true" : "false",
            _ => value.ToString() ?? "—"
        };
    }
}