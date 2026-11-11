namespace geoDC.UiDriver;

/// <summary>
///     The options that shape output rather than what a verb does: <c>--format json|text</c> and
///     <c>--append &lt;path&gt;</c> for any verb, and <c>--report &lt;path.md&gt;</c> for <c>do</c> and
///     <c>rebuild --then</c>. <see cref="Extract" /> takes them out of the arguments before the verb parses the rest.
/// </summary>
/// <param name="Format">How lines are printed.</param>
/// <param name="ReportPath">Where to write a Markdown report of the run, or null for none.</param>
/// <param name="AppendPath">
///     A file every line of output is also appended to (see <see cref="OutputTee" />), or null for none.
/// </param>
public sealed record DriverOutputOptions(DriverOutputFormat Format, string? ReportPath, string? AppendPath = null)
{
    /// <summary>
    ///     Removes <c>--format</c>, <c>--report</c> and <c>--append</c> and their values from <paramref name="args" />.
    ///     For <c>do</c> they must come before the first step, like its other options, so a step's own arguments are
    ///     not mistaken for them. For every other verb they may appear anywhere among its options.
    ///     <paramref name="appendFromEnvironment" /> (<c>GEODC_UI_APPEND</c>) is the append file when
    ///     <c>--append</c> is not given. <paramref name="defaultFormat" /> is the format when <c>--format</c> is not
    ///     given (see <see cref="DefaultFormatFor" />).
    /// </summary>
    public static (string[] Args, DriverOutputOptions Options) Extract(IReadOnlyList<string> args,
        string? appendFromEnvironment = null, DriverOutputFormat defaultFormat = DriverOutputFormat.Json)
    {
        string? environmentAppend = string.IsNullOrWhiteSpace(appendFromEnvironment) ? null : appendFromEnvironment;
        if (args.Count == 0)
        {
            return ([], new DriverOutputOptions(defaultFormat, null, environmentAppend));
        }

        string verb = args[0].ToLowerInvariant();
        List<string> rest = [args[0]];
        string? format = null;
        string? report = null;
        string? append = null;
        int i = 1;
        while (i < args.Count)
        {
            string token = args[i];
            bool leadingOption = token.StartsWith("--", StringComparison.Ordinal);
            if (verb == "do" && !leadingOption)
            {
                // The first step: everything from here on belongs to the steps.
                break;
            }

            string option = token.ToLowerInvariant();
            if (option is "--format" or "--report" or "--append")
            {
                if (i + 1 >= args.Count)
                {
                    throw new DriverException("bad-arguments", $"Option '{token}' expects a value.", 1);
                }

                switch (option)
                {
                    case "--format":
                        format = args[i + 1];
                        break;
                    case "--report":
                        report = args[i + 1];
                        break;
                    default:
                        append = args[i + 1];
                        break;
                }

                i += 2;
                continue;
            }

            int width = CliArgs.IsFlag(token) || !leadingOption ? 1 : 2;
            for (int k = 0; k < width && i < args.Count; k++)
            {
                rest.Add(args[i++]);
            }
        }

        while (i < args.Count)
        {
            rest.Add(args[i++]);
        }

        DriverOutputFormat parsedFormat = format?.ToLowerInvariant() switch
        {
            null => defaultFormat,
            "json" => DriverOutputFormat.Json,
            "text" => DriverOutputFormat.Text,
            _ => throw new DriverException("bad-arguments",
                $"Option '--format' expects 'json' or 'text' but got '{format}'.", 1)
        };

        if (report is not null && verb is not ("do" or "rebuild"))
        {
            throw new DriverException("bad-arguments",
                "--report applies only to do and rebuild --then, which run steps to report.", 1);
        }

        return ([.. rest], new DriverOutputOptions(parsedFormat, report, append ?? environmentAppend));
    }

    /// <summary>
    ///     The format used when <c>--format</c> is not given: readable text for a person at a terminal, and the
    ///     one-line JSON contract when stdout is piped or captured, so <c>| ConvertFrom-Json</c> and scripts keep working.
    /// </summary>
    public static DriverOutputFormat DefaultFormatFor(bool outputRedirected)
    {
        return outputRedirected ? DriverOutputFormat.Json : DriverOutputFormat.Text;
    }
}