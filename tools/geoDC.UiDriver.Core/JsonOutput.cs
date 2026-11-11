using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace geoDC.UiDriver;

/// <summary>
///     Emits the single-line JSON contract on stdout. Human-readable detail (e.g. tree text)
///     is printed by the verbs after this line. Diagnostics go to stderr.
/// </summary>
/// <remarks>
///     Every line, success, failure or <c>do</c> summary, ends with a top-level <c>warning</c> field when the source
///     given to <see cref="WarnFrom" /> reports one, and has no such field otherwise. A step line of a <c>do</c> run
///     whose step came from a script carries <c>from</c> (see <see cref="SetStepOrigin" />) before any warning.
///     <para>
///         With <see cref="Format" /> set to <see cref="DriverOutputFormat.Text" />, each line is printed as
///         <see cref="ConsoleTextFormatter" /> renders it instead, with a warning shown once, above the first line that
///         carries it. Either way, a line is also passed to <see cref="DoReport.Active" /> when a report is being
///         kept. The JSON a line would print is the same in both cases, so the formats cannot drift apart.
///     </para>
/// </remarks>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static Func<string?>? _warningSource;
    private static bool _warningRead;
    private static string? _warning;
    private static IReadOnlyList<string>? _stepOrigin;
    private static long? _stepStarted;
    private static bool _warningShown;
    private static DriverOutputFormat _format = DriverOutputFormat.Json;

    /// <summary>How lines are printed; <see cref="DriverOutputFormat.Json" /> unless <c>--format text</c> was given.</summary>
    public static DriverOutputFormat Format
    {
        get => _format;
        set
        {
            _format = value;
            _warningShown = false;
            if (value == DriverOutputFormat.Text)
            {
                // The text format uses ✓ and ✗; a legacy console code page would print them as '?'.
                Console.OutputEncoding = new UTF8Encoding(false);
            }
        }
    }

    /// <summary>
    ///     Sets where the <c>warning</c> field comes from, or clears it with null. The source is called once, when the
    ///     first line after this call is written, so it sees the state the command left behind (a relaunched or killed
    ///     instance, for example); every later line repeats its answer. A source that throws adds no warning.
    /// </summary>
    public static void WarnFrom(Func<string?>? source)
    {
        _warningSource = source;
        _warningRead = false;
        _warning = null;
    }

    /// <summary>
    ///     Sets the <c>from</c> field added to the lines that follow: the <c>path:line</c> chain a <c>do</c> step came
    ///     from, outermost script first. Null or empty adds no field.
    /// </summary>
    public static void SetStepOrigin(IReadOnlyList<string>? from)
    {
        _stepOrigin = from is { Count: > 0 } ? from : null;
    }

    /// <summary>
    ///     Marks the start of a <c>do</c> step, so its line can show how long it took in the text format and the
    ///     report. <see cref="EndStep" /> clears it. The JSON line does not carry the time.
    /// </summary>
    public static void BeginStep()
    {
        _stepStarted = Stopwatch.GetTimestamp();
    }

    /// <summary>Clears the step started by <see cref="BeginStep" />.</summary>
    public static void EndStep()
    {
        _stepStarted = null;
    }

    /// <summary>
    ///     A success line, <c>{ ok: true, verb, step?, data }</c>, with <paramref name="data" /> serialized with the
    ///     web (camelCase) defaults. <c>step</c> is present only for a step of a <c>do</c> run.
    /// </summary>
    /// <param name="verb">The verb the line reports on.</param>
    /// <param name="data">The verb's result, emitted as <c>data</c>.</param>
    /// <param name="step">The 1-based number of the <c>do</c> step, or null outside a <c>do</c> run.</param>
    public static void Success(string verb, object data, int? step = null)
    {
        object line = step is null
            ? new { ok = true, verb, data }
            : new { ok = true, verb, step, data };
        Write(line);
    }

    /// <summary>
    ///     An action verb's success line, with the outcome of its <c>--await</c> / <c>--await-gone</c> waits added
    ///     as <c>data.awaited</c> when there were any. Without waits the field is left out, so plain actions keep
    ///     their shape.
    /// </summary>
    public static void Success(string verb, object data, int? step, IReadOnlyList<object> awaited)
    {
        if (awaited.Count == 0)
        {
            Success(verb, data, step);
            return;
        }

        JsonObject merged = JsonSerializer.SerializeToNode(data, Options) as JsonObject ?? [];
        merged["awaited"] = JsonSerializer.SerializeToNode(awaited, Options);
        Success(verb, merged, step);
    }

    /// <summary>
    ///     A failure line. Optional fields are added to <c>error</c> only when they carry something, so a failure
    ///     without them keeps the bare <c>code</c> + <c>message</c> shape.
    /// </summary>
    /// <param name="verb">The verb the line reports on.</param>
    /// <param name="code">The machine-readable error code, emitted as <c>error.code</c>.</param>
    /// <param name="message">The human-readable explanation, emitted as <c>error.message</c>.</param>
    /// <param name="candidates">Near-match candidates, emitted as <c>error.candidates</c> when non-empty.</param>
    /// <param name="step">The 1-based number of the <c>do</c> step, or null outside a <c>do</c> run.</param>
    /// <param name="state">A structured observation, emitted as <c>error.state</c> when not null.</param>
    /// <param name="lookup">
    ///     Element lookup hints, emitted as <c>error.closest</c>, <c>error.elsewhere</c>, <c>error.matches</c> and
    ///     <c>error.hint</c>, each only when it carries something.
    /// </param>
    /// <param name="details">
    ///     An object whose properties are added to <c>error</c> as they are, after the standard fields; null adds none.
    /// </param>
    public static void Failure(string verb, string code, string message, IReadOnlyList<string>? candidates = null,
        int? step = null, object? state = null, ElementLookupHints? lookup = null, object? details = null)
    {
        JsonObject error = new() { ["code"] = code, ["message"] = message };
        if (candidates is { Count: > 0 })
        {
            error["candidates"] = JsonSerializer.SerializeToNode(candidates, Options);
        }

        if (state is not null)
        {
            error["state"] = JsonSerializer.SerializeToNode(state, Options);
        }

        if (lookup is not null)
        {
            AddLookup(error, lookup);
        }

        if (details is not null && JsonSerializer.SerializeToNode(details, Options) is JsonObject extra)
        {
            foreach ((string name, JsonNode? value) in extra.ToList())
            {
                extra.Remove(name);
                error[name] = value;
            }
        }

        object line = step is null
            ? new { ok = false, verb, error }
            : new { ok = false, verb, step, error };
        Write(line);
    }

    private static void AddLookup(JsonObject error, ElementLookupHints lookup)
    {
        if (lookup.Closest.Count > 0)
        {
            error["closest"] = JsonSerializer.SerializeToNode(lookup.Closest, Options);
        }

        if (lookup.Elsewhere.Count > 0)
        {
            error["elsewhere"] = JsonSerializer.SerializeToNode(lookup.Elsewhere, Options);
        }

        if (lookup.Matches.Count > 0)
        {
            error["matches"] = JsonSerializer.SerializeToNode(lookup.Matches, Options);
        }

        if (lookup.Hint is not null)
        {
            error["hint"] = lookup.Hint;
        }
    }

    /// <summary>
    ///     The last line of a <c>do</c> run: how many steps it had, ran and passed, the script it came from, and the
    ///     first failure (null when every step passed). Steps count after <c>run</c> steps are expanded. <c>ok</c>
    ///     mirrors whether the run passed, so a reader that only looks at the last line still gets the verdict. With a
    ///     report being kept, <c>report</c> gives its path.
    /// </summary>
    public static void DoSummary(int steps, int run, int passed, string? script, object? firstFailure)
    {
        object summary = DoReport.Active is { } report
            ? new { steps, run, passed, script, firstFailure, report = report.Path }
            : new { steps, run, passed, script, firstFailure };
        Write(new { ok = firstFailure is null, verb = "do", summary });
    }

    private static void Write(object line)
    {
        string? warning = CurrentWarning();
        DoReport? report = DoReport.Active;
        if (_format == DriverOutputFormat.Json && report is null)
        {
            Console.WriteLine(Serialize(line, warning));
            return;
        }

        string json = Serialize(line, warning);
        JsonObject node = JsonNode.Parse(json) as JsonObject ?? [];
        long? elapsedMs = _stepStarted is { } started && node["step"] is not null
            ? (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            : null;
        if (_format == DriverOutputFormat.Json)
        {
            Console.WriteLine(json);
        }
        else
        {
            // ConsoleTextFormatter separates a line's parts with '\n'; write them with the platform's line ending, as
            // Console.WriteLine ends the last one, so a Windows console never gets a mix of the two.
            Console.WriteLine(ConsoleTextFormatter.Format(node, elapsedMs, !_warningShown).ReplaceLineEndings());
            _warningShown |= warning is not null;
        }

        report?.Record(node, elapsedMs);
    }

    /// <summary>The line's JSON with <c>from</c> and <c>warning</c> added when they apply.</summary>
    private static string Serialize(object line, string? warning)
    {
        if ((warning is null && _stepOrigin is null) ||
            JsonSerializer.SerializeToNode(line, Options) is not JsonObject extended)
        {
            return JsonSerializer.Serialize(line, Options);
        }

        if (_stepOrigin is not null)
        {
            extended["from"] = JsonSerializer.SerializeToNode(_stepOrigin, Options);
        }

        if (warning is not null)
        {
            extended["warning"] = warning;
        }

        return extended.ToJsonString(Options);
    }

    private static string? CurrentWarning()
    {
        if (_warningRead)
        {
            return _warning;
        }

        _warningRead = true;
        try
        {
            _warning = _warningSource?.Invoke();
        }
        catch (Exception ex)
        {
            // The warning decorates the line; a failure to work it out must not lose the line itself.
            Console.Error.WriteLine($"geodc-ui: could not work out the output warning: {ex.Message}");
            _warning = null;
        }

        return _warning;
    }
}

/// <summary>How the UI driver prints its result lines.</summary>
public enum DriverOutputFormat
{
    /// <summary>One JSON object per line, the contract scripts and tools read.</summary>
    Json,

    /// <summary>Lines for a person: see <see cref="ConsoleTextFormatter" />.</summary>
    Text
}