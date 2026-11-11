using System.Text;
using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

// Script files: parsing, tokenising, variable substitution, and the expansion of run steps into the called steps.
public sealed partial record DoInvocation
{
    /// <summary>
    ///     Parses a script's text into its optional <c>@target</c> and its steps, one per line, with variables
    ///     substituted. With <paramref name="rejectUnused" />, a variable in <paramref name="variables" /> that the
    ///     script neither defaults nor references fails the parse, so a misspelt <c>--var</c> or <c>run</c> argument
    ///     is caught.
    /// </summary>
    private static ParsedScript ParseScript(string text, IReadOnlyDictionary<string, string> variables,
        string source, bool rejectUnused)
    {
        string? target = null;
        Dictionary<string, string> defaults = new(StringComparer.Ordinal);
        List<ScriptLine> stepLines = [];
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            int lineNumber = index + 1;
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // @var takes the rest of the line as written, so a default may contain spaces without quoting.
            string[] tokens = line.StartsWith("@var ", StringComparison.OrdinalIgnoreCase)
                ? ["@var", line[5..].Trim()]
                : Tokenize(line, source, lineNumber);
            if (tokens[0].StartsWith('@'))
            {
                string directive = tokens[0].ToLowerInvariant();
                if (directive is not ("@target" or "@var") || tokens.Length != 2)
                {
                    throw new DriverException("bad-script",
                        $"{source}:{lineNumber}: directives are '@target app|sample' and '@var name=value'.", 1);
                }

                if (stepLines.Count > 0)
                {
                    throw new DriverException("bad-script",
                        $"{source}:{lineNumber}: '{directive}' must come before the first step.", 1);
                }

                if (directive == "@target")
                {
                    if (target is not null)
                    {
                        throw new DriverException("bad-script", $"{source}:{lineNumber}: '@target' is given twice.", 1);
                    }

                    target = tokens[1];
                }
                else
                {
                    (string name, string value) = ParseVariable(tokens[1]);
                    if (!defaults.TryAdd(name, value))
                    {
                        throw new DriverException("bad-script",
                            $"{source}:{lineNumber}: variable '{name}' is given a default twice.", 1);
                    }
                }

                continue;
            }

            if (tokens.Contains("++"))
            {
                throw new DriverException("bad-script",
                    $"{source}:{lineNumber}: a script takes one step per line; split the '++' onto its own lines.",
                    1);
            }

            stepLines.Add(new ScriptLine(tokens, lineNumber));
        }

        if (stepLines.Count == 0)
        {
            throw new DriverException("bad-script", $"{source}: the script has no steps.", 1);
        }

        if (rejectUnused)
        {
            HashSet<string> referenced = new(defaults.Keys, StringComparer.Ordinal);
            foreach (ScriptLine line in stepLines)
            {
                foreach (string token in line.Tokens)
                {
                    foreach (Match match in VariableReference.Matches(token))
                    {
                        referenced.Add(match.Groups[1].Value);
                    }
                }
            }

            string[] unused = [.. variables.Keys.Where(name => !referenced.Contains(name)).Order(StringComparer.Ordinal)];
            if (unused.Length > 0)
            {
                string known = referenced.Count == 0
                    ? "it takes no variables"
                    : "it takes " + string.Join(", ", referenced.Order(StringComparer.Ordinal));
                throw new DriverException("bad-script",
                    $"{source}: variable(s) {string.Join(", ", unused)} are passed but never used; {known}.", 1);
            }
        }

        foreach ((string name, string value) in variables)
        {
            defaults[name] = value;
        }

        List<ScriptLine> steps =
        [
            .. stepLines.Select(line => line with
            {
                Tokens = [.. line.Tokens.Select(token => Substitute(token, defaults, source, line.LineNumber))]
            })
        ];
        return new ParsedScript(target, steps, defaults);
    }

    /// <summary>Splits one script line into tokens, grouping quoted text.</summary>
    public static string[] Tokenize(string line, string source, int lineNumber)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool inToken = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                i++;
                continue;
            }

            inToken = true;
            if (c is '"' or '\'')
            {
                int close = i + 1;
                while (true)
                {
                    close = line.IndexOf(c, close);
                    if (close < 0)
                    {
                        throw new DriverException("bad-script",
                            $"{source}:{lineNumber}: unterminated {(c == '"' ? "double" : "single")} quote.", 1);
                    }

                    // '' inside single quotes is one literal quote.
                    if (c == '\'' && close + 1 < line.Length && line[close + 1] == '\'')
                    {
                        current.Append(line, i + 1, close - i);
                        i = close + 1;
                        close = i + 1;
                        continue;
                    }

                    break;
                }

                current.Append(line, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }

            current.Append(c);
            i++;
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }

    private static string Substitute(string token, IReadOnlyDictionary<string, string> variables, string source,
        int lineNumber)
    {
        return VariableReference.Replace(token, match =>
        {
            string name = match.Groups[1].Value;
            return variables.TryGetValue(name, out string? value)
                ? value
                : throw new DriverException("bad-script",
                    $"{source}:{lineNumber}: variable '{name}' is not defined; pass --var {name}=<value>.", 1);
        });
    }

    private static (string Name, string Value) ParseVariable(string raw)
    {
        int equals = raw.IndexOf('=');
        string name = equals < 0 ? raw : raw[..equals];
        if (equals < 0 || !VariableName.IsMatch(name))
        {
            throw new DriverException("bad-arguments",
                $"Option '--var' expects name=value with a name of letters, digits, '_' or '-', got '{raw}'.", 1);
        }

        return (name, raw[(equals + 1)..]);
    }

    private sealed record ScriptLine(string[] Tokens, int LineNumber);

    private sealed record ParsedScript(
        string? Target,
        IReadOnlyList<ScriptLine> Steps,
        IReadOnlyDictionary<string, string> Variables);

    /// <summary>Replaces <c>run</c> steps with the steps of the scripts they call, recursively.</summary>
    private sealed class ScriptExpander(Func<string, string> readFile, string? target)
    {
        private readonly string _target = (target ?? DriverTarget.App.Name).ToLowerInvariant();

        /// <summary>
        ///     Adds one step, or the steps of the script it runs, to <paramref name="steps" />.
        ///     <paramref name="from" /> is the step's own location chain, outermost first. <paramref name="location" />
        ///     identifies the step in errors. <paramref name="stack" /> holds the normalised paths of the scripts being
        ///     expanded, outermost first.
        /// </summary>
        public void Add(string[] tokens, IReadOnlyList<string> from, string? callerPath, string location,
            IReadOnlyList<string> stack, List<DoStep> steps)
        {
            if (!string.Equals(tokens[0], "run", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new DoStep(tokens, from));
                return;
            }

            string code = callerPath is null ? "bad-arguments" : "bad-script";
            if (tokens.Length < 2 || tokens[1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new DriverException(code,
                    $"{location}: 'run' expects a script and then name=value arguments, e.g. " +
                    "'run lib/app-goto-page page=CalibrationTab'.", 1);
            }

            Dictionary<string, string> arguments = new(StringComparer.Ordinal);
            foreach (string raw in tokens[2..])
            {
                (string name, string value) parsed;
                try
                {
                    parsed = ParseVariable(raw);
                }
                catch (DriverException)
                {
                    throw new DriverException(code,
                        $"{location}: 'run' arguments are name=value, got '{raw}'.", 1);
                }

                if (!arguments.TryAdd(parsed.name, parsed.value))
                {
                    throw new DriverException(code, $"{location}: 'run' passes '{parsed.name}' twice.", 1);
                }
            }

            string path = ResolveScriptPath(tokens[1], callerPath);
            string chain = string.Join(" < ", from.Reverse().Append(location).Distinct());
            if (stack.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                throw new DriverException("bad-script",
                    $"{location}: '{path}' runs itself: {string.Join(" -> ", stack.Append(path))}.", 1);
            }

            if (stack.Count >= MaxRunDepth)
            {
                throw new DriverException("bad-script",
                    $"{location}: 'run' steps nest more than {MaxRunDepth} deep: {string.Join(" -> ", stack.Append(path))}.",
                    1);
            }

            ParsedScript script;
            try
            {
                script = ParseScript(readFile(path), arguments, path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DriverException("script-not-found",
                    $"{location}: could not read UI driver script '{path}': {ex.Message}", 1);
            }
            catch (DriverException ex)
            {
                throw new DriverException(ex.Code, $"{ex.Message} (run from {chain})", ex.ExitCode);
            }

            if (script.Target is not null && !string.Equals(script.Target, _target, StringComparison.OrdinalIgnoreCase))
            {
                throw new DriverException("bad-script",
                    $"{location}: '{path}' is written for --target {script.Target}, but this run targets {_target}.",
                    1);
            }

            IReadOnlyList<string> nestedStack = [.. stack, path];
            foreach (ScriptLine line in script.Steps)
            {
                string lineLocation = $"{path}:{line.LineNumber}";
                Add(line.Tokens, [.. from, lineLocation], path, lineLocation, nestedStack, steps);
            }
        }
    }
}