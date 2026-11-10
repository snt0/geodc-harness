namespace geoDC.UiDriver;

/// <summary>
///     One read of an element's automation state, taken while polling a <c>wait</c> condition. Pattern-derived
///     fields are null when the element does not support that pattern, so "not toggleable" stays distinct from
///     "toggled off". Serialized as-is into the <c>error.state</c> of a timed-out wait.
/// </summary>
/// <param name="Present">False when no element with the AutomationId was found (or its handle went stale).</param>
/// <param name="Offscreen">The UIA <c>IsOffscreen</c> property; false when the element is absent.</param>
/// <param name="Width">The bounding rectangle's width in screen pixels; 0 when absent or not laid out.</param>
/// <param name="Height">The bounding rectangle's height in screen pixels; 0 when absent or not laid out.</param>
/// <param name="Enabled">The UIA <c>IsEnabled</c> property; null when the element is absent or unreadable.</param>
/// <param name="Name">The UIA Name; null when the element is absent or the read failed.</param>
/// <param name="Value">The ValuePattern value; null when the element has no ValuePattern.</param>
/// <param name="Toggle"><c>on</c>, <c>off</c>, or <c>indeterminate</c>; null without a TogglePattern.</param>
/// <param name="Selected">The SelectionItemPattern state; null without that pattern.</param>
/// <param name="Expansion">
///     <c>expanded</c>, <c>collapsed</c>, <c>partially-expanded</c>, or <c>leaf</c>; null without an
///     ExpandCollapsePattern.
/// </param>
/// <param name="ValueUnreadable">
///     True when the element supports the ValuePattern but reading it failed this poll, so <c>--value-matches</c>
///     must not fall back to Name as it would for an element with no ValuePattern.
/// </param>
/// <param name="Unreadable">
///     True when the element was found this poll but reading its state threw (a busy provider, or a handle that
///     went stale after the search). It counts as present but meets no condition, so the poll is retried rather
///     than read as gone.
/// </param>
public sealed record ObservedElementState(
    bool Present,
    bool Offscreen = false,
    int Width = 0,
    int Height = 0,
    bool? Enabled = null,
    string? Name = null,
    string? Value = null,
    string? Toggle = null,
    bool? Selected = null,
    string? Expansion = null,
    bool ValueUnreadable = false,
    bool Unreadable = false)
{
    /// <summary>No element with the AutomationId was found this poll; every other field has its default.</summary>
    public static ObservedElementState Absent { get; } = new(false);

    /// <summary>Found by the search, but its state could not be read this poll.</summary>
    public static ObservedElementState UnreadableNow { get; } = new(true, Unreadable: true);

    /// <summary>Present, on screen, and occupying space: what a bare <c>wait --id</c> means by "ready".</summary>
    public bool IsLaidOut => Present && !Unreadable && !Offscreen && Width > 0 && Height > 0;

    /// <summary>
    ///     Absent, or still in the tree but offscreen with no area. WPF can keep a closed popup's or a collapsed
    ///     element's peer around briefly, and that leftover is not something a user can see.
    /// </summary>
    public bool IsGone => !Present || (!Unreadable && Offscreen && (Width <= 0 || Height <= 0));

    /// <summary>
    ///     The text <c>--value-matches</c> tests: the ValuePattern value, or Name when there is none. Null when the
    ///     ValuePattern exists but could not be read, which leaves the condition unmet for that poll.
    /// </summary>
    public string? MatchText => ValueUnreadable ? null : Value ?? Name ?? string.Empty;

    /// <summary>
    ///     A short human-readable summary for a timed-out wait's message, such as
    ///     <c>enabled, onscreen, 120x24, value="3.5"</c>, or <c>absent</c> or <c>present, state unreadable</c> when
    ///     there is no state to show. Pattern-derived parts appear only when the element supports the pattern.
    /// </summary>
    public string Describe()
    {
        if (!Present)
        {
            return "absent";
        }

        if (Unreadable)
        {
            return "present, state unreadable";
        }

        List<string> parts =
        [
            Enabled switch { true => "enabled", false => "disabled", null => "enabled=?" },
            Offscreen ? "offscreen" : "onscreen",
            $"{Width}x{Height}"
        ];

        if (Value is not null)
        {
            parts.Add($"value=\"{Value}\"");
        }
        else if (ValueUnreadable)
        {
            parts.Add("value=?");
        }

        if (Name is { Length: > 0 })
        {
            parts.Add($"name=\"{Name}\"");
        }

        if (Toggle is not null)
        {
            parts.Add($"toggle={Toggle}");
        }

        if (Selected is { } selected)
        {
            parts.Add(selected ? "selected" : "unselected");
        }

        if (Expansion is not null)
        {
            parts.Add(Expansion);
        }

        return string.Join(", ", parts);
    }
}

/// <summary>A satisfied wait: the matching elements' final states, whether the first poll already met it, and the time it took.</summary>
public sealed record WaitResult(IReadOnlyList<ObservedElementState> Observed, bool MetOnFirstLook, int WaitedMs);