using System.Text;

namespace geoDC.UiDriver;

/// <summary>
///     The state of a node's stateful supported patterns, read once. Each member is null (or false) when the node does
///     not support the pattern, so a state that a pattern reports is never confused with a missing pattern.
/// </summary>
/// <param name="Value">The ValuePattern value, unescaped and untruncated; null without a ValuePattern.</param>
/// <param name="Toggled"><c>on</c>, <c>off</c> or <c>mixed</c>; null without a TogglePattern.</param>
/// <param name="Selected">The SelectionItemPattern IsSelected; false without the pattern.</param>
/// <param name="Expanded">
///     <c>expanded</c>, <c>partial</c> or <c>collapsed</c>; null without an ExpandCollapsePattern or for a leaf node.
/// </param>
/// <param name="Scroll">
///     The ScrollPattern position as <c>h=&lt;percent&gt; v=&lt;percent&gt;</c>, with <c>n/a</c> for an axis that
///     does not scroll; null without a ScrollPattern.
/// </param>
public sealed record UiaNodeState(
    string? Value = null,
    string? Toggled = null,
    bool Selected = false,
    string? Expanded = null,
    string? Scroll = null)
{
    /// <summary>The longest value <see cref="Format" /> prints before truncating with an ellipsis.</summary>
    public const int PrintedValueLength = 80;

    /// <summary>The state of a node with no stateful pattern.</summary>
    public static readonly UiaNodeState None = new();

    /// <summary>
    ///     The state tokens a <c>tree</c> line prints, separated by two spaces:
    ///     <c>value: "12.5"  toggled: on  selected  expanded  scroll: h=n/a v=40%</c>. A value is escaped and
    ///     truncated to <see cref="PrintedValueLength" /> characters. Default states (not selected, leaf node) print
    ///     nothing, which keeps <c>grep selected</c> meaningful. Value and toggle print whenever the pattern is
    ///     supported, because leaving them out would be ambiguous. Empty when there is nothing to print.
    /// </summary>
    public string Format()
    {
        List<string> tokens = [];
        if (Value is not null)
        {
            tokens.Add($"value: \"{Printable(Value, PrintedValueLength)}\"");
        }

        if (Toggled is not null)
        {
            tokens.Add($"toggled: {Toggled}");
        }

        if (Selected)
        {
            tokens.Add("selected");
        }

        switch (Expanded)
        {
            case "expanded":
                tokens.Add("expanded");
                break;
            case "partial":
                tokens.Add("expanded(partial)");
                break;
            case "collapsed":
                tokens.Add("collapsed");
                break;
        }

        if (Scroll is not null)
        {
            tokens.Add($"scroll: {Scroll}");
        }

        return string.Join("  ", tokens);
    }

    /// <summary>
    ///     <paramref name="value" /> on one line: CR, LF, tab and double quote escaped, other control characters
    ///     replaced by a space, and cut at <paramref name="maxLength" /> characters with a trailing ellipsis.
    /// </summary>
    public static string Printable(string value, int maxLength)
    {
        StringBuilder builder = new(Math.Min(value.Length, maxLength));
        foreach (char c in value)
        {
            if (builder.Length >= maxLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(c switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                _ => char.IsControl(c) ? " " : c.ToString()
            });
        }

        return builder.ToString();
    }
}