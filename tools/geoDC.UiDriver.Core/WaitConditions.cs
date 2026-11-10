using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

/// <summary>How a poll of every element matching an AutomationId compares with the requested conditions.</summary>
public enum WaitOutcome
{
    /// <summary>The conditions hold for this poll, so the wait can end.</summary>
    Met,

    /// <summary>The conditions do not hold yet; the wait polls again until its timeout.</summary>
    Unmet,

    /// <summary>More than one element is present, so a per-element condition has no single subject.</summary>
    Ambiguous
}

/// <summary>
///     The conditions a <c>wait</c> polls for (and an <c>assert</c> checks), parsed from its flags. With no flag the
///     element must be laid out (present, on screen, non-zero bounds), as a bare <c>wait --id</c> requires.
///     <c>--gone</c> cannot be combined with other flags. The state flags (<c>--exists</c>, <c>--enabled</c>,
///     <c>--disabled</c>, <c>--value</c>, <c>--value-matches</c>, <c>--name</c>, <c>--toggled</c>,
///     <c>--selected</c>, <c>--expanded</c>) require the element to be present and combine with AND. They do not
///     require it to be on screen, because a button scrolled out of view can still become enabled.
/// </summary>
public sealed class WaitConditions
{
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(1);

    private static readonly string[] ToggleStates = ["on", "off", "indeterminate"];

    private readonly bool? _enabled;
    private readonly bool _exists;
    private readonly bool _expanded;
    private readonly string? _name;
    private readonly bool _selected;
    private readonly string? _toggle;
    private readonly string? _value;
    private readonly Regex? _valuePattern;

    private WaitConditions(bool gone, bool exists = false, bool? enabled = null, string? value = null,
        Regex? valuePattern = null, string? name = null, string? toggle = null, bool selected = false,
        bool expanded = false)
    {
        IsGone = gone;
        _exists = exists;
        _enabled = enabled;
        _value = value;
        _valuePattern = valuePattern;
        _name = name;
        _toggle = toggle;
        _selected = selected;
        _expanded = expanded;
    }

    /// <summary>The bare <c>wait --id</c> condition: present and laid out.</summary>
    public static WaitConditions Ready { get; } = new(false);

    /// <summary>The <c>--gone</c> condition on its own.</summary>
    public static WaitConditions Gone { get; } = new(true);

    /// <summary>True for the <c>--gone</c> condition, which excludes every state condition.</summary>
    public bool IsGone { get; }

    private bool IsExistsOnly => _exists && _enabled is null && _value is null && _valuePattern is null &&
                                 _name is null && _toggle is null && !_selected && !_expanded;

    /// <summary>
    ///     True when no flag was given, so the condition is the bare <c>wait --id</c> one: the element is present and
    ///     laid out (<see cref="ObservedElementState.IsLaidOut" />).
    /// </summary>
    public bool IsReady => !IsGone && !_exists && _enabled is null && _value is null && _valuePattern is null &&
                           _name is null && _toggle is null && !_selected && !_expanded;

    /// <summary>Human- and JSON-facing names of the conditions, e.g. <c>["enabled", "toggled on"]</c>.</summary>
    public IReadOnlyList<string> Labels
    {
        get
        {
            if (IsGone)
            {
                return ["gone"];
            }

            if (IsReady)
            {
                return ["ready"];
            }

            List<string> labels = [];
            if (_exists)
            {
                labels.Add("exists");
            }

            if (_enabled is { } enabled)
            {
                labels.Add(enabled ? "enabled" : "disabled");
            }

            if (_value is not null)
            {
                labels.Add($"value \"{_value}\"");
            }

            if (_valuePattern is not null)
            {
                labels.Add($"value-matches /{_valuePattern}/");
            }

            if (_name is not null)
            {
                labels.Add($"name \"{_name}\"");
            }

            if (_toggle is not null)
            {
                labels.Add($"toggled {_toggle}");
            }

            if (_selected)
            {
                labels.Add("selected");
            }

            if (_expanded)
            {
                labels.Add("expanded");
            }

            return labels;
        }
    }

    /// <summary>
    ///     Reads the condition flags from <paramref name="cli" />: <see cref="Gone" /> for <c>--gone</c>,
    ///     <see cref="Ready" /> when no condition flag is given, otherwise the AND of the given state flags.
    ///     <c>--toggled</c> is case-insensitive; <c>--value</c> and <c>--name</c> match exactly.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> (exit code 1) for <c>--enabled</c> with <c>--disabled</c>, <c>--value</c> with
    ///     <c>--value-matches</c>, <c>--gone</c> with any state flag, a <c>--toggled</c> other than <c>on</c>,
    ///     <c>off</c> or <c>indeterminate</c>, or an invalid <c>--value-matches</c> expression.
    /// </exception>
    public static WaitConditions Parse(CliArgs cli)
    {
        bool gone = cli.Has("--gone");
        bool exists = cli.Has("--exists");
        bool enabled = cli.Has("--enabled");
        bool disabled = cli.Has("--disabled");
        string? value = cli.Value("--value");
        string? valueRegex = cli.Value("--value-matches");
        string? name = cli.Value("--name");
        string? toggleRaw = cli.Value("--toggled");
        bool selected = cli.Has("--selected");
        bool expanded = cli.Has("--expanded");

        if (enabled && disabled)
        {
            throw new DriverException("bad-arguments", "--enabled and --disabled cannot be combined.", 1);
        }

        if (value is not null && valueRegex is not null)
        {
            throw new DriverException("bad-arguments",
                "--value and --value-matches cannot be combined; anchor the expression to match exactly.", 1);
        }

        bool anyState = exists || enabled || disabled || value is not null || valueRegex is not null ||
                        name is not null || toggleRaw is not null || selected || expanded;
        if (gone && anyState)
        {
            throw new DriverException("bad-arguments",
                "--gone cannot be combined with state conditions: an absent element has no state to test.", 1);
        }

        if (gone)
        {
            return Gone;
        }

        string? toggle = null;
        if (toggleRaw is not null)
        {
            toggle = toggleRaw.ToLowerInvariant();
            if (!ToggleStates.Contains(toggle))
            {
                throw new DriverException("bad-arguments",
                    $"Option '--toggled' expects on, off, or indeterminate but got '{toggleRaw}'.", 1);
            }
        }

        Regex? valuePattern = null;
        if (valueRegex is not null)
        {
            try
            {
                valuePattern = new Regex(valueRegex, RegexOptions.CultureInvariant, RegexBudget);
            }
            catch (ArgumentException ex)
            {
                throw new DriverException("bad-arguments",
                    $"Option '--value-matches' is not a valid regular expression: {ex.Message}", 1);
            }
        }

        bool? enabledCondition = enabled ? true : disabled ? false : null;
        return new WaitConditions(false, exists, enabledCondition, value, valuePattern, name, toggle, selected,
            expanded);
    }

    /// <summary>
    ///     Evaluates one poll. <paramref name="observed" /> holds the state of every element currently matching the
    ///     AutomationId (empty when none does). <c>--gone</c> needs every match gone. The other conditions need
    ///     exactly one present match that satisfies them.
    /// </summary>
    public WaitOutcome Evaluate(IReadOnlyList<ObservedElementState> observed)
    {
        if (IsGone)
        {
            return observed.All(state => state.IsGone) ? WaitOutcome.Met : WaitOutcome.Unmet;
        }

        List<ObservedElementState> present = [.. observed.Where(state => state.Present)];
        return present.Count switch
        {
            0 => WaitOutcome.Unmet,
            1 => IsSatisfiedBy(present[0]) ? WaitOutcome.Met : WaitOutcome.Unmet,
            _ => WaitOutcome.Ambiguous
        };
    }

    /// <summary>
    ///     Whether the one element read as <paramref name="state" /> meets every condition. A state that is present but
    ///     unreadable meets only a bare <c>--exists</c>. <c>--value</c> and <c>--value-matches</c> test
    ///     <see cref="ObservedElementState.MatchText" />.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> (exit code 1) when <c>--value-matches</c> runs past its one-second evaluation budget.
    /// </exception>
    public bool IsSatisfiedBy(ObservedElementState state)
    {
        if (IsGone)
        {
            return state.IsGone;
        }

        if (IsReady)
        {
            return state.IsLaidOut;
        }

        if (!state.Present)
        {
            return false;
        }

        // Being found is all --exists asks, so a failed state read still meets it on its own. Any other
        // condition needs the state, so an unreadable poll is retried.
        if (state.Unreadable)
        {
            return IsExistsOnly;
        }

        if (_enabled is { } enabled && state.Enabled != enabled)
        {
            return false;
        }

        if (_toggle is not null && !string.Equals(state.Toggle, _toggle, StringComparison.Ordinal))
        {
            return false;
        }

        if (_selected && state.Selected != true)
        {
            return false;
        }

        if (_expanded && !string.Equals(state.Expansion, "expanded", StringComparison.Ordinal))
        {
            return false;
        }

        if (_name is not null && !string.Equals(state.Name ?? string.Empty, _name, StringComparison.Ordinal))
        {
            return false;
        }

        if (_value is not null && !string.Equals(state.MatchText, _value, StringComparison.Ordinal))
        {
            return false;
        }

        return _valuePattern is null || (state.MatchText is { } text && MatchesValue(_valuePattern, text));
    }

    private static bool MatchesValue(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new DriverException("bad-arguments",
                $"Option '--value-matches' took longer than {RegexBudget.TotalSeconds:0}s to evaluate; " +
                "simplify the expression.", 1);
        }
    }
}

/// <summary>One <c>--await</c> / <c>--await-gone</c> target attached to an action verb.</summary>
public sealed record AwaitRequest(string ElementId, WaitConditions Conditions);

/// <summary>
///     The waits an action verb (<c>click</c>, <c>settext</c>, <c>select</c>, <c>key</c>) runs after acting, so the
///     action and the check that it took effect are one step. <c>--await-gone</c> runs before <c>--await</c>, because
///     one view usually closes before the next appears. Each wait gets the full <see cref="Timeout" /> instead of
///     sharing one budget.
/// </summary>
public sealed record AwaitPlan(IReadOnlyList<AwaitRequest> Requests, TimeSpan Timeout)
{
    /// <summary>The seconds each wait gets when no <c>--await-timeout</c> is given.</summary>
    public const double DefaultTimeoutSeconds = 5;

    /// <summary>True when the action has neither <c>--await</c> nor <c>--await-gone</c>, so it runs no waits.</summary>
    public bool IsEmpty => Requests.Count == 0;

    /// <summary>
    ///     Reads <c>--await-gone</c>, <c>--await</c> and <c>--await-timeout</c> from <paramref name="cli" />. Each await
    ///     waits for the plain condition (<see cref="WaitConditions.Gone" /> or <see cref="WaitConditions.Ready" />).
    ///     The timeout is in seconds, and is <see cref="DefaultTimeoutSeconds" /> when not given.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> (exit code 1) for <c>--await-timeout</c> without an await, or a timeout that is not a
    ///     non-negative number of seconds.
    /// </exception>
    public static AwaitPlan Parse(CliArgs cli)
    {
        List<AwaitRequest> requests = [];
        if (cli.Value("--await-gone") is { } goneId)
        {
            requests.Add(new AwaitRequest(goneId, WaitConditions.Gone));
        }

        if (cli.Value("--await") is { } readyId)
        {
            requests.Add(new AwaitRequest(readyId, WaitConditions.Ready));
        }

        if (requests.Count == 0 && cli.Has("--await-timeout"))
        {
            throw new DriverException("bad-arguments",
                "--await-timeout applies only with --await or --await-gone.", 1);
        }

        return new AwaitPlan(requests, cli.Seconds("--await-timeout", DefaultTimeoutSeconds));
    }
}