using System.Globalization;
using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

/// <summary>One error MSBuild reported.</summary>
/// <param name="File">
///     The file the error is in, or the tool that raised it (<c>CSC</c>, <c>MSBUILD</c>) when it has no source location;
///     null when the line names neither.
/// </param>
/// <param name="Line">The 1-based line, when the error has a location.</param>
/// <param name="Column">The 1-based column, when the error has one.</param>
/// <param name="Code">The error code, such as <c>CS1002</c> or <c>MSB3027</c>.</param>
/// <param name="Message">The error text.</param>
/// <param name="Project">The project that reported it, from the trailing <c>[project]</c>, when there is one.</param>
public sealed record BuildError(string? File, int? Line, int? Column, string Code, string Message, string? Project);

/// <summary>The errors of a build: the first few in the order reported, and how many distinct errors there were.</summary>
public sealed record BuildErrors(IReadOnlyList<BuildError> First, int Count)
{
    /// <summary>How many errors a failed <c>rebuild</c> reports.</summary>
    public const int DefaultLimit = 10;

    // MSBuild's canonical message format: `origin: [subcategory] error CODE: text [project]`, where the origin is
    // `file(line,col)`, `file(line,col,endLine,endCol)`, `file`, a tool name, or absent.
    private static readonly Regex ErrorLine = new(
        @"^(?:(?<origin>.*?)\s*:\s*)?(?:[^:]*?\s+)?(?:fatal\s+)?error\s+(?<code>[A-Za-z]+[A-Za-z0-9]*\d)\s*:\s*(?<message>.*?)(?:\s+\[(?<project>[^\[\]]+)\])?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Location = new(@"^(?<file>.*?)\((?<line>\d+)(?:,(?<column>\d+))?(?:,\d+,\d+)?\)$",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Picks the error lines out of MSBuild output, in order, counting each distinct line once: the console logger
    ///     repeats every error in its summary. Warnings and other lines are ignored.
    /// </summary>
    public static BuildErrors Parse(IEnumerable<string> lines, int limit = DefaultLimit)
    {
        List<BuildError> first = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int count = 0;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (TryParse(line) is not { } error || !seen.Add(line))
            {
                continue;
            }

            count++;
            if (first.Count < limit)
            {
                first.Add(error);
            }
        }

        return new BuildErrors(first, count);
    }

    /// <summary>Parses one MSBuild error line, or returns null for any other line.</summary>
    public static BuildError? TryParse(string line)
    {
        Match match = ErrorLine.Match(line.Trim());
        if (!match.Success)
        {
            return null;
        }

        string? origin = match.Groups["origin"].Success && match.Groups["origin"].Length > 0
            ? match.Groups["origin"].Value
            : null;
        string? file = origin;
        int? lineNumber = null;
        int? column = null;
        if (origin is not null && Location.Match(origin) is { Success: true } location)
        {
            file = location.Groups["file"].Value;
            lineNumber = int.Parse(location.Groups["line"].Value, CultureInfo.InvariantCulture);
            column = location.Groups["column"].Success
                ? int.Parse(location.Groups["column"].Value, CultureInfo.InvariantCulture)
                : null;
        }

        string? project = match.Groups["project"].Success ? match.Groups["project"].Value : null;
        return new BuildError(file, lineNumber, column, match.Groups["code"].Value, match.Groups["message"].Value,
            project);
    }
}