using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace geoDC.UiDriver;

/// <summary>
///     A Markdown walkthrough of one <c>do</c> run (<c>do --report &lt;path.md&gt;</c>, or <c>rebuild --then … --report</c>):
///     the script and its variables, a table with one row per step that ran (command, outcome, time, and the script
///     line it came from), the first failure in full, and every screenshot the run took, embedded by a path relative
///     to the report. It is fed the same JSON lines the run prints, so it reports what the output says, including a
///     <c>stale-build</c> warning, which qualifies everything the run checked.
/// </summary>
/// <remarks>
///     Rows are recorded as lines are written and the file is written once, by <see cref="Save" />, whether the run
///     passed, failed at a step, or failed before its first step (an <c>attach</c> that found nothing, for
///     example), so a failed run still leaves a report up to where it stopped.
/// </remarks>
public sealed class DoReport(string path, DoInvocation invocation, DateTime startedUtc)
{
    private readonly List<Row> _rows = [];
    private JsonObject? _summary;
    private JsonObject? _failure;
    private string? _warning;

    /// <summary>The report of the run in progress, which <see cref="JsonOutput" /> feeds; null when none was asked for.</summary>
    public static DoReport? Active { get; set; }

    /// <summary>Where the report is written, as given. A relative path resolves against the <see cref="DriverHome" />.</summary>
    public string Path { get; } = path;

    /// <summary>
    ///     Records one written line. Step lines become rows, the <c>do</c> summary becomes the result, and a failure
    ///     with no step (the run could not start) is reported as the run's failure. Other lines are ignored.
    /// </summary>
    public void Record(JsonObject line, long? elapsedMs)
    {
        _warning ??= line["warning"]?.GetValue<string>();
        string? verb = line["verb"]?.GetValue<string>();
        bool ok = line["ok"]?.GetValue<bool>() == true;
        if (verb == "do" && line["summary"] is JsonObject summary)
        {
            _summary = summary;
            return;
        }

        if (line["step"] is not JsonValue stepValue || !stepValue.TryGetValue(out int step))
        {
            if (!ok)
            {
                _failure ??= line;
            }

            return;
        }

        _rows.Add(new Row(step, verb ?? "?", ok, line["data"] as JsonObject, line["error"] as JsonObject,
            line["from"] is JsonArray from ? [.. from.Select(entry => entry?.GetValue<string>() ?? string.Empty)] : [],
            elapsedMs));
        if (!ok)
        {
            _failure ??= line;
        }
    }

    /// <summary>Writes the report, creating its folder; an I/O failure is reported on stderr, not thrown.</summary>
    public void Save()
    {
        try
        {
            string fullPath = DriverHome.Resolve(Path);
            string folder = System.IO.Path.GetDirectoryName(fullPath) ?? DriverHome.Directory;
            Directory.CreateDirectory(folder);
            File.WriteAllText(fullPath, Render(folder, DriverHome.Resolve));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"geodc-ui: could not write the report {Path}: {ex.Message}");
        }
    }

    /// <summary>
    ///     The report's Markdown. <paramref name="reportFolder" /> is the folder the report is written to;
    ///     <paramref name="fullPath" /> turns a screenshot path as the UI driver reported it into a full path, so its
    ///     image link can be made relative to that folder.
    /// </summary>
    public string Render(string reportFolder, Func<string, string> fullPath)
    {
        StringBuilder md = new();
        string title = invocation.ScriptPath is { } script ? System.IO.Path.GetFileName(script) : "inline steps";
        md.Append("# UI driver run: ").Append(title).Append("\n\n");
        md.Append("| | |\n|---|---|\n");
        md.Append("| Result | ").Append(Cell(Result())).Append(" |\n");
        if (_warning is not null)
        {
            md.Append("| Warning | ").Append(Cell(DescribeWarning(_warning))).Append(" |\n");
        }

        md.Append("| Target | ").Append(invocation.Target ?? DriverTarget.App.Name).Append(" |\n");
        if (invocation.ScriptPath is { } scriptPath)
        {
            md.Append("| Script | ").Append(Code(scriptPath)).Append(" |\n");
        }

        if (invocation.Variables.Count > 0)
        {
            md.Append("| Variables | ").Append(string.Join("<br>", invocation.Variables
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Code(pair.Key)} = {Cell(pair.Value)}"))).Append(" |\n");
        }

        md.Append("| Started | ").Append(startedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC |\n");

        md.Append("\n## Steps\n\n");
        if (_rows.Count == 0)
        {
            md.Append("No step ran.\n");
        }
        else
        {
            md.Append("| # | Step | Result | Time | From |\n|---|---|---|---|---|\n");
            foreach (Row row in _rows)
            {
                string command = row.Step - 1 < invocation.Steps.Count
                    ? string.Join(' ', invocation.Steps[row.Step - 1].Tokens.Select(Quote))
                    : row.Verb;
                string outcome = row.Ok
                    ? "✓ " + ConsoleTextFormatter.Describe(row.Verb, row.Data)
                    : "✗ " + (row.Error?["code"]?.GetValue<string>() ?? "error");
                string time = row.ElapsedMs is { } ms ? $"{ms.ToString(CultureInfo.InvariantCulture)} ms" : string.Empty;
                string from = row.From.Count > 0 ? Code(Shorten(row.From[^1])) : string.Empty;
                md.Append("| ").Append(row.Step).Append(" | ").Append(Code(command)).Append(" | ")
                    .Append(Cell(outcome)).Append(" | ").Append(time).Append(" | ").Append(from).Append(" |\n");
            }
        }

        if (_failure is not null)
        {
            AppendFailure(md, _failure);
        }

        List<Row> screenshots =
        [
            .. _rows.Where(row => row.Ok && row.Verb == "screenshot" && row.Data?["path"] is JsonValue)
        ];
        if (screenshots.Count > 0)
        {
            md.Append("\n## Screenshots\n");
            foreach (Row row in screenshots)
            {
                string image = row.Data!["path"]!.GetValue<string>();
                string relative = System.IO.Path.GetRelativePath(reportFolder, fullPath(image)).Replace('\\', '/');
                string subject = row.Data["elementId"]?.GetValue<string>() is { } id ? $": {id}" : string.Empty;
                md.Append("\n### Step ").Append(row.Step).Append(subject).Append("\n\n")
                    .Append("![Step ").Append(row.Step).Append(" screenshot](").Append(relative.Replace(" ", "%20"))
                    .Append(")\n");
            }
        }

        return md.ToString();
    }

    private string Result()
    {
        if (_summary is null)
        {
            return _failure is not null ? "✗ Failed before the first step" : "Did not finish";
        }

        long steps = _summary["steps"]?.GetValue<long>() ?? 0;
        long run = _summary["run"]?.GetValue<long>() ?? 0;
        long passed = _summary["passed"]?.GetValue<long>() ?? 0;
        if (_summary["firstFailure"] is not JsonObject failure)
        {
            return $"✓ Passed: {passed} of {steps} steps";
        }

        string notRun = steps > run ? $", {steps - run} not run" : string.Empty;
        return $"✗ Failed at step {failure["step"]}: {passed} of {steps} passed{notRun}";
    }

    private static string DescribeWarning(string warning)
    {
        return warning == "stale-build"
            ? "⚠ stale-build: the app was older than its build output, so this run checked the old code."
            : "⚠ " + warning;
    }

    private static void AppendFailure(StringBuilder md, JsonObject line)
    {
        JsonObject? error = line["error"] as JsonObject;
        string verb = line["verb"]?.GetValue<string>() ?? "?";
        string where = line["step"] is JsonValue step ? $"Step {step}, " : string.Empty;
        md.Append("\n## Failure\n\n**").Append(where).Append(Code(verb)).Append(": ")
            .Append(error?["code"]?.GetValue<string>() ?? "error").Append("**\n\n");
        if (error?["message"]?.GetValue<string>() is { } message)
        {
            md.Append(message.Replace("\n", "  \n")).Append("\n\n");
        }

        if (line["from"] is JsonArray from && from.Count > 0)
        {
            md.Append("- From: ").Append(string.Join(" < ", from.Reverse().Select(entry =>
                Code(Shorten(entry?.GetValue<string>() ?? string.Empty))))).Append('\n');
        }

        if (error?["hint"]?.GetValue<string>() is { } hint)
        {
            md.Append("- Hint: ").Append(hint).Append('\n');
        }

        foreach (string field in new[] { "closest", "elsewhere", "candidates" })
        {
            if (error?[field] is JsonArray items && items.Count > 0)
            {
                md.Append("- ").Append(char.ToUpperInvariant(field[0])).Append(field[1..]).Append(": ")
                    .Append(string.Join(", ", items.Select(item => Code(item?.GetValue<string>() ?? string.Empty))))
                    .Append('\n');
            }
        }

        if (error?["state"] is JsonObject state)
        {
            md.Append("- Observed: ").Append(Code(state.ToJsonString())).Append('\n');
        }
    }

    private static string Quote(string token)
    {
        return token.Length == 0 || token.Any(char.IsWhiteSpace) ? $"'{token.Replace("'", "''")}'" : token;
    }

    private static string Shorten(string location)
    {
        string prefix = DoInvocation.ScriptRoot + "/";
        return location.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? location[prefix.Length..] : location;
    }

    /// <summary>Text safe inside a table cell: pipes escaped, line breaks as <c>&lt;br&gt;</c>.</summary>
    private static string Cell(string text)
    {
        return text.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    /// <summary>A code span that survives backticks in its text, safe inside a table cell.</summary>
    private static string Code(string text)
    {
        string fence = text.Contains('`') ? "``" : "`";
        string padded = text.StartsWith('`') || text.EndsWith('`') ? $" {text} " : text;
        return fence + Cell(padded) + fence;
    }

    private sealed record Row(
        int Step,
        string Verb,
        bool Ok,
        JsonObject? Data,
        JsonObject? Error,
        IReadOnlyList<string> From,
        long? ElapsedMs);
}