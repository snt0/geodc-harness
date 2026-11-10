using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace geoDC.UiDriver;

/// <summary>
///     One UIA control-view element read once, with the children that were walked under it. It holds everything the
///     <c>tree</c> and <c>find</c> output prints, so filtering, matching and rendering work on plain data without a
///     live automation connection. It round-trips through System.Text.Json, so <c>tree</c> can store it for a later
///     <c>tree --diff</c>.
/// </summary>
/// <param name="ControlType">The UIA control type name, such as <c>Button</c>, or <c>Unknown</c> when unreadable.</param>
/// <param name="AutomationId">The AutomationId, or null when the element has none.</param>
/// <param name="Name">The UIA Name; empty when the element has none.</param>
/// <param name="Bounds">The bounding rectangle in physical screen pixels; empty when unreadable.</param>
/// <param name="IsEnabled">The UIA IsEnabled property.</param>
/// <param name="IsOffscreen">The UIA IsOffscreen property.</param>
/// <param name="State">The state of the stateful supported patterns (value, toggled, selected, expanded, scroll).</param>
/// <param name="Patterns">The supported patterns, named as in <see cref="PatternNames" />, in that order.</param>
/// <param name="Children">The walked children; empty for a leaf or for a node at the depth limit.</param>
/// <param name="RuntimeId">
///     The UIA runtime id joined with commas, or null when unreadable. It identifies an element reached through two
///     roots, such as an owned popup that is both a top-level window and a child of its owner.
/// </param>
public sealed record UiaNodeSnapshot(
    string ControlType,
    string? AutomationId,
    string Name,
    [property: JsonConverter(typeof(RectangleJsonConverter))]
    Rectangle Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    UiaNodeState State,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<UiaNodeSnapshot> Children,
    string? RuntimeId = null)
{
    /// <summary>The patterns the UI driver reports, in the order a node line lists them.</summary>
    public static readonly IReadOnlyList<string> PatternNames =
        ["Invoke", "Value", "Toggle", "Selection", "SelectionItem", "ExpandCollapse", "Scroll", "ScrollItem"];

    /// <summary>
    ///     The patterns that make a node something a script can act on or read state from. Selection (a container's
    ///     view of its items) and ScrollItem (present on every generated item) alone do not qualify.
    /// </summary>
    public static readonly IReadOnlyList<string> InteractablePatterns =
        ["Invoke", "Toggle", "Value", "SelectionItem", "ExpandCollapse", "Scroll"];

    /// <summary>True when <see cref="AutomationId" /> is neither null nor empty; not serialized.</summary>
    [JsonIgnore]
    public bool HasAutomationId => AutomationId is { Length: > 0 };

    /// <summary>
    ///     True when the node supports at least one of <see cref="InteractablePatterns" />, whatever its enabled or
    ///     offscreen state; not serialized.
    /// </summary>
    [JsonIgnore]
    public bool IsInteractable =>
        Patterns.Any(pattern => InteractablePatterns.Contains(pattern, StringComparer.Ordinal));

    /// <summary>
    ///     The node's single-line description, as <c>tree</c> prints it:
    ///     <c>[Button] id: SaveButton  name: "Save"  bounds=(10,20 80x24)  enabled  patterns: Invoke</c>. A node
    ///     without an AutomationId prints <c>id: —</c>, so a dump doubles as the annotation worklist.
    /// </summary>
    public string FormatLine()
    {
        StringBuilder line = new();
        line.Append($"[{ControlType}] id: {(HasAutomationId ? AutomationId : "—")}");
        if (Name.Length > 0)
        {
            line.Append($"  name: \"{Name}\"");
        }

        line.Append($"  bounds=({FormatBounds(Bounds)})");
        line.Append(IsEnabled ? "  enabled" : "  disabled");
        if (IsOffscreen)
        {
            line.Append("  offscreen");
        }

        string state = State.Format();
        if (state.Length > 0)
        {
            line.Append($"  {state}");
        }

        line.Append($"  patterns: {(Patterns.Count > 0 ? string.Join("/", Patterns) : "—")}");
        return line.ToString();
    }

    /// <summary>A rectangle as a <c>tree</c> line prints it: <c>x,y wxh</c> in physical screen pixels.</summary>
    public static string FormatBounds(Rectangle bounds)
    {
        return $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}";
    }

    /// <summary>The number of nodes below this one, at every level.</summary>
    public int CountDescendants()
    {
        return Children.Sum(child => 1 + child.CountDescendants());
    }

    /// <summary>
    ///     Appends this node and its children, one line each, indented two spaces per level below
    ///     <paramref name="level" />. Returns the number of lines written.
    /// </summary>
    public int WriteTree(StringBuilder builder, int level = 0)
    {
        builder.Append(' ', level * 2);
        builder.AppendLine(FormatLine());
        int lines = 1;
        foreach (UiaNodeSnapshot child in Children)
        {
            lines += child.WriteTree(builder, level + 1);
        }

        return lines;
    }
}

/// <summary>
///     Writes a <see cref="Rectangle" /> as the array <c>[x, y, width, height]</c> and reads it back, so a stored
///     snapshot holds the four values rather than every derived property of the struct.
/// </summary>
public sealed class RectangleJsonConverter : JsonConverter<Rectangle>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">The array does not hold exactly four integers.</exception>
    public override Rectangle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int[] values = JsonSerializer.Deserialize<int[]>(ref reader, options) ?? [];
        return values.Length == 4
            ? new Rectangle(values[0], values[1], values[2], values[3])
            : throw new JsonException($"A rectangle is [x, y, width, height] but {values.Length} values were given.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Rectangle value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}