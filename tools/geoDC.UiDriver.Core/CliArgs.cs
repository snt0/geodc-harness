using System.Globalization;

namespace geoDC.UiDriver;

/// <summary>
///     Minimal argument parser: first token is the verb, `--name value` pairs follow.
///     Boolean flags (no value) are a closed set so option values may contain anything.
/// </summary>
public sealed class CliArgs
{
    private static readonly HashSet<string> BooleanFlags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--launch", "--main-window", "--passive", "--active", "--gone", "--exists", "--enabled", "--disabled", "--selected", "--expanded",
            "--interactable", "--diff", "--bounds", "--interactive", "--lifecycle"
        };

    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Parses <paramref name="args" />: the first token is the verb and every later token must be an option name
    ///     starting with <c>--</c>, followed by a value unless it is one of the boolean flags. A repeated option keeps
    ///     its last value.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> (exit code 1) for a positional argument after the verb or a valued option with no value.
    /// </exception>
    public CliArgs(string[] args)
    {
        Verb = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

        for (int i = 1; i < args.Length; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new DriverException("bad-arguments", $"Unexpected positional argument '{token}'.", 1);
            }

            if (BooleanFlags.Contains(token))
            {
                _options[token] = null;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new DriverException("bad-arguments", $"Option '{token}' expects a value.", 1);
            }

            _options[token] = args[++i];
        }
    }

    /// <summary>The first argument in lower case, or an empty string when there are no arguments.</summary>
    public string Verb { get; }

    /// <summary>Whether <paramref name="token" /> is an option that takes no value.</summary>
    public static bool IsFlag(string token)
    {
        return BooleanFlags.Contains(token);
    }

    /// <summary>
    ///     Whether option <paramref name="name" /> (including its <c>--</c> prefix, case-insensitive) was given, as a
    ///     flag or with a value.
    /// </summary>
    public bool Has(string name)
    {
        return _options.ContainsKey(name);
    }

    /// <summary>
    ///     The value of option <paramref name="name" /> (including its <c>--</c> prefix, case-insensitive), or null when
    ///     it was not given or is a boolean flag.
    /// </summary>
    public string? Value(string name)
    {
        return _options.TryGetValue(name, out string? value) ? value : null;
    }

    /// <summary>The value of option <paramref name="name" />, which must have been given with a value.</summary>
    /// <exception cref="DriverException"><c>bad-arguments</c> (exit code 1) when the option has no value.</exception>
    public string Require(string name)
    {
        return Value(name) ?? throw new DriverException("bad-arguments", $"Option '{name}' is required.", 1);
    }

    /// <summary>
    ///     The value of option <paramref name="name" /> as an invariant-culture integer, or
    ///     <paramref name="fallback" /> when it has no value.
    /// </summary>
    /// <exception cref="DriverException"><c>bad-arguments</c> (exit code 1) when the value is not an integer.</exception>
    public int Int(string name, int fallback)
    {
        if (Value(name) is not { } raw)
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new DriverException("bad-arguments", $"Option '{name}' expects an integer but got '{raw}'.", 1);
    }

    /// <summary>
    ///     The <c>--timeout</c> option in seconds, or <paramref name="fallbackSeconds" /> when it has no value; parsed
    ///     as <see cref="Seconds" /> does.
    /// </summary>
    /// <exception cref="DriverException"><c>bad-arguments</c> (exit code 1) when the value is not valid seconds.</exception>
    public TimeSpan Timeout(double fallbackSeconds)
    {
        return Seconds("--timeout", fallbackSeconds);
    }

    /// <summary>
    ///     The value of option <paramref name="name" /> as a duration given in seconds (an invariant-culture number,
    ///     fractions allowed), or <paramref name="fallbackSeconds" /> when it has no value.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> (exit code 1) when the value is not a finite, non-negative number.
    /// </exception>
    public TimeSpan Seconds(string name, double fallbackSeconds)
    {
        if (Value(name) is not { } raw)
        {
            return TimeSpan.FromSeconds(fallbackSeconds);
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
               seconds >= 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : throw new DriverException("bad-arguments", $"Option '{name}' expects seconds but got '{raw}'.", 1);
    }
}