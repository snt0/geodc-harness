using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

/// <summary>
///     A parsed <c>do</c> invocation: the target and the steps to run against one shared session. Steps come either
///     inline, as verb/option groups separated by <c>++</c>, or from a script file (<c>--file</c>), never both.
///     <para>
///         A script holds one step per line, in the same grammar as an inline step. Blank lines and lines whose
///         first non-blank character is <c>#</c> are skipped. Tokens split on whitespace, and single or double quotes
///         group a token that contains spaces. Nothing inside quotes is escaped, so a regex keeps its backslashes
///         (<c>'^\d+ m$'</c>). Inside single quotes, <c>''</c> is a literal quote.
///     </para>
///     <para>
///         <c>${name}</c> is replaced by the value of <c>--var name=value</c>. An undefined variable fails the parse
///         instead of running a step with a hole in it, and so does a <c>--var</c> the script never uses. Two
///         directives may come before the first step. <c>@target app|sample</c> says which target the script was
///         written for, so running it against the other one fails before any step runs. <c>@var name=value</c> gives
///         a variable a default that <c>--var</c> overrides. Its value is the rest of the line, unquoted.
///     </para>
///     <para>
///         A <c>run &lt;script&gt; [name=value ...]</c> step, inline or in a script, is replaced by the called
///         script's steps when the invocation is parsed. A script that cannot be read or parsed therefore fails the
///         invocation before any step runs.
///     </para>
///     <para>
///         <c>&lt;script&gt;</c> resolves against <see cref="ScriptRoot" />, except when it starts with <c>./</c> or
///         <c>../</c>, already starts with <see cref="ScriptRoot" />, or is rooted. A <c>./</c> or <c>../</c> path is
///         relative to the calling script's folder, or to the <see cref="DriverHome" /> for an inline step. <c>.ui</c>
///         is added when the name has no extension. The resolved path is read relative to the driver home.
///     </para>
///     <para>
///         The called script sees only the variables its <c>run</c> passes and its own <c>@var</c> defaults. The
///         parse fails when a <c>run</c> passes a variable the script never uses, when the script's
///         <c>@target</c> differs from the invocation's, or when a script runs itself, directly or through others.
///     </para>
/// </summary>
public sealed partial record DoInvocation(string? Target, IReadOnlyList<DoStep> Steps, string? ScriptPath)
{
    /// <summary>The folder, relative to the <see cref="DriverHome" />, that <c>run</c> resolves script names against.</summary>
    public const string ScriptRoot = "dev/ui-scripts";

    /// <summary>How deeply <c>run</c> steps may nest, as a backstop when one script reaches itself by two spellings.</summary>
    public const int MaxRunDepth = 16;

    private static readonly Regex VariableReference = new(@"\$\{([A-Za-z_][A-Za-z0-9_-]*)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex VariableName = new("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The top-level script's variables as its steps saw them: its <c>@var</c> defaults with <c>--var</c> values
    ///     over them. Empty for inline steps.
    /// </summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Whether <c>--interactive</c> was given: a <c>pause</c> step then waits for the developer instead of passing
    ///     straight away (see <see cref="PauseStep" />).
    /// </summary>
    public bool Interactive { get; init; }

    /// <summary>Each step's tokens, without where it came from.</summary>
    public IReadOnlyList<string[]> StepTokens => [.. Steps.Select(step => step.Tokens)];

    /// <summary>
    ///     Parses <c>do</c>'s arguments, starting after the verb. <paramref name="readFile" /> loads a script. It is
    ///     injected so the grammar can be tested without touching the file system.
    /// </summary>
    public static DoInvocation Parse(IReadOnlyList<string> args, Func<string, string> readFile)
    {
        int i = 0;
        string? target = null;
        string? file = null;
        bool interactive = false;
        Dictionary<string, string> variables = new(StringComparer.Ordinal);
        while (i < args.Count && args[i].StartsWith("--", StringComparison.Ordinal))
        {
            string option = args[i].ToLowerInvariant();
            if (option == "--interactive")
            {
                interactive = true;
                i++;
                continue;
            }

            if (option is not ("--target" or "--file" or "--var"))
            {
                throw new DriverException("bad-arguments",
                    $"Only --target, --file, --var and --interactive may precede the first step, got '{args[i]}'.", 1);
            }

            if (i + 1 >= args.Count)
            {
                throw new DriverException("bad-arguments", $"Option '{args[i]}' expects a value.", 1);
            }

            string value = args[i + 1];
            switch (option)
            {
                case "--target":
                    target = value;
                    break;
                case "--file":
                    file = value;
                    break;
                default:
                    (string name, string variable) = ParseVariable(value);
                    if (!variables.TryAdd(name, variable))
                    {
                        throw new DriverException("bad-arguments", $"Variable '{name}' is defined more than once.", 1);
                    }

                    break;
            }

            i += 2;
        }

        if (file is null)
        {
            if (variables.Count > 0)
            {
                throw new DriverException("bad-arguments",
                    "--var applies only with --file; inline steps already contain their values.", 1);
            }

            List<string[]> inline = SplitInline([.. args.Skip(i)]);
            ScriptExpander inlineExpander = new(readFile, target);
            List<DoStep> inlineSteps = [];
            for (int index = 0; index < inline.Count; index++)
            {
                inlineExpander.Add(inline[index], [], null, $"step {index + 1}", [], inlineSteps);
            }

            return new DoInvocation(target, inlineSteps, null) { Interactive = interactive };
        }

        if (i < args.Count)
        {
            throw new DriverException("bad-arguments",
                $"--file runs the script's steps only; remove the inline steps starting at '{args[i]}'.", 1);
        }

        string text;
        try
        {
            text = readFile(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DriverException("script-not-found", $"Could not read UI driver script '{file}': {ex.Message}", 1);
        }

        ParsedScript script = ParseScript(text, variables, file, true);
        if (script.Target is not null && target is not null &&
            !string.Equals(script.Target, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new DriverException("bad-arguments",
                $"Script '{file}' is written for --target {script.Target}, but --target {target} was given.", 1);
        }

        string? effectiveTarget = target ?? script.Target;
        ScriptExpander expander = new(readFile, effectiveTarget);
        List<DoStep> steps = [];
        foreach (ScriptLine line in script.Steps)
        {
            expander.Add(line.Tokens, [$"{file}:{line.LineNumber}"], file, $"{file}:{line.LineNumber}",
                [NormalizePath(file)], steps);
        }

        return new DoInvocation(effectiveTarget, steps, file) { Variables = script.Variables, Interactive = interactive };
    }

    /// <summary>Splits inline <c>do</c> arguments on <c>++</c> into steps.</summary>
    public static List<string[]> SplitInline(IReadOnlyList<string> tokens)
    {
        List<string[]> steps = [];
        List<string> current = [];
        foreach (string token in tokens)
        {
            if (token == "++")
            {
                steps.Add([.. current]);
                current.Clear();
            }
            else
            {
                current.Add(token);
            }
        }

        steps.Add([.. current]);

        if (steps.Any(step => step.Length == 0))
        {
            throw new DriverException("bad-arguments",
                "Empty step: `do` expects verb/option groups separated by '++', e.g. " +
                "`do click --id SetupTab ++ screenshot --id SetupPage`, or --file <script>.", 1);
        }

        return steps;
    }

    /// <summary>
    ///     Resolves a <c>run</c> step's script name to the path it is read from, with <c>/</c> separators and
    ///     <c>.</c> / <c>..</c> segments collapsed. A name that already starts with <see cref="ScriptRoot" /> is kept
    ///     as it is. <paramref name="callerPath" /> is the calling script, or null for an inline step or a
    ///     <c>rebuild --then</c> script.
    /// </summary>
    public static string ResolveScriptPath(string name, string? callerPath)
    {
        string withExtension = Path.HasExtension(name) ? name : name + ".ui";
        string slashed = withExtension.Replace('\\', '/');
        string combined;
        if (Path.IsPathRooted(withExtension))
        {
            combined = slashed;
        }
        else if (slashed.StartsWith("./", StringComparison.Ordinal) ||
                 slashed.StartsWith("../", StringComparison.Ordinal))
        {
            string? folder = callerPath is null ? null : Path.GetDirectoryName(callerPath.Replace('\\', '/'));
            combined = string.IsNullOrEmpty(folder) ? slashed : folder.Replace('\\', '/') + "/" + slashed;
        }
        else if (slashed.StartsWith(ScriptRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            combined = slashed;
        }
        else
        {
            combined = ScriptRoot + "/" + slashed;
        }

        return NormalizePath(combined);
    }

    private static string NormalizePath(string path)
    {
        string slashed = path.Replace('\\', '/');
        bool rooted = slashed.StartsWith('/');
        List<string> segments = [];
        foreach (string segment in slashed.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return (rooted ? "/" : string.Empty) + string.Join('/', segments);
    }
}

/// <summary>
///     One step of a <c>do</c> run: its tokens (verb first) and where it came from, as <c>path:line</c> entries
///     from the outermost script to the line itself. An inline step has no entries; an inline <c>run</c>'s steps
///     start at the called script.
/// </summary>
public sealed record DoStep(string[] Tokens, IReadOnlyList<string> From);