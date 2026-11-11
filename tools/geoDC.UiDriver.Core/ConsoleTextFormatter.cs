using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace geoDC.UiDriver;

/// <summary>
///     Formats a UI driver JSON line, as <see cref="JsonOutput" /> builds it, as text for a person reading a terminal:
///     ✓ or ✗, the step number, the verb and a one-line description, with a failure's code, message and lookup hints
///     on the lines below. It reads only the line, so the JSON line remains the full record of what happened.
/// </summary>
public static class ConsoleTextFormatter
{
    /// <summary>The prefix stripped from script paths in <c>from</c>, so a location reads <c>lib/x.ui:3</c>.</summary>
    private const string ScriptRootPrefix = DoInvocation.ScriptRoot + "/";

    private const int ListLimit = 5;

    /// <summary>
    ///     Formats <paramref name="line" />. <paramref name="elapsedMs" /> is how long a <c>do</c> step took, shown
    ///     after its description; null shows none. <paramref name="includeWarning" /> adds the line's
    ///     <c>warning</c>, if it has one, on a line of its own above it.
    /// </summary>
    public static string Format(JsonObject line, long? elapsedMs, bool includeWarning)
    {
        StringBuilder text = new();
        if (includeWarning && Text(line["warning"]) is { } warning)
        {
            text.Append("! ").Append(DescribeWarning(warning)).Append('\n');
        }

        string verb = Text(line["verb"]) ?? "?";
        if (verb == "do" && line["summary"] is JsonObject summary)
        {
            text.Append(RenderSummary(summary));
            return text.ToString();
        }

        bool ok = line["ok"]?.GetValue<bool>() == true;
        int? step = Number(line["step"]) is { } s ? (int)s : null;
        text.Append(ok ? "✓ " : "✗ ");
        if (step is not null)
        {
            text.Append(step.Value.ToString(CultureInfo.InvariantCulture).PadLeft(2)).Append(' ');
        }

        text.Append(verb);
        if (ok)
        {
            string description = Describe(verb, line["data"] as JsonObject);
            if (description.Length > 0)
            {
                text.Append(' ').Append(description);
            }
        }
        else if (line["error"] is JsonObject error)
        {
            text.Append(": ").Append(Text(error["code"]) ?? "error");
        }

        if (elapsedMs is not null)
        {
            text.Append(" (").Append(elapsedMs.Value.ToString(CultureInfo.InvariantCulture)).Append(" ms)");
        }

        if (Location(line["from"]) is { } location)
        {
            text.Append("  · ").Append(location);
        }

        if (!ok && line["error"] is JsonObject failure)
        {
            AppendErrorDetail(text, failure);
        }

        return text.ToString();
    }

    /// <summary>
    ///     A successful line's data as a short phrase: what was acted on, checked or produced, plus what an action
    ///     awaited. A verb without its own phrase lists its scalar fields as <c>name=value</c>.
    /// </summary>
    public static string Describe(string verb, JsonObject? data)
    {
        if (data is null)
        {
            return string.Empty;
        }

        string id = Text(data["elementId"]) ?? string.Empty;
        string description = verb switch
        {
            "click" or "scrollto" => id,
            "settext" => $"{id} = \"{Text(data["value"])}\"",
            "select" => $"\"{Text(data["item"])}\" in {Text(data["containerId"])}",
            "key" => Text(data["keys"]) ?? string.Empty,
            "wait" or "assert" => Join(id, Conditions(data["conditions"])),
            "scroll" => Join(id, Text(data["direction"]), Text(data["amount"])),
            "screenshot" => Join(Text(data["path"]),
                $"({Number(data["width"])}x{Number(data["height"])})",
                Text(data["elementId"]) is { } shot ? $"of {shot}" : null),
            "tree" => DescribeTree(data),
            "find" => Plural(Number(data["matches"]), "match", "matches"),
            "source" => DescribeSource(data),
            "audit" => Plural(Number(data["count"]), "finding", "findings"),
            "settle" => $"{Number(data["ms"])} ms",
            "pause" => DescribePause(data),
            "logs" => $"{Number(data["tailed"])} of {Number(data["totalLines"])} lines from {Text(data["path"])}",
            "attach" => DescribeSession(data),
            "status" => DescribeStatus(data),
            "rebuild" => DescribeRebuild(data),
            "kill" => data["wasRunning"]?.GetValue<bool>() == true
                ? $"stopped pid {Number(data["pid"])}"
                : $"nothing was running for {Text(data["target"])}",
            _ => Scalars(data)
        };

        return description + Awaited(data["awaited"]);
    }

    /// <summary>
    ///     A <c>source</c> line: <c>4 texts, 3 from the strings files (1 ambiguous), 1 not</c>, or
    ///     <c>no text</c>.
    /// </summary>
    private static string DescribeSource(JsonObject data)
    {
        long texts = Number(data["texts"]) ?? 0;
        if (texts == 0)
        {
            return "no text";
        }

        long ambiguous = Number(data["ambiguous"]) ?? 0;
        string ambiguity = ambiguous > 0 ? $" ({ambiguous} ambiguous)" : string.Empty;
        return $"{Plural(texts, "text", "texts")}, {Number(data["sourced"]) ?? 0} from the strings files{ambiguity}, " +
               $"{Number(data["unsourced"]) ?? 0} not";
    }

    private static string RenderSummary(JsonObject summary)
    {
        long steps = Number(summary["steps"]) ?? 0;
        long run = Number(summary["run"]) ?? 0;
        long passed = Number(summary["passed"]) ?? 0;
        string script = Text(summary["script"]) is { } path ? $" ({Shorten(path)})" : string.Empty;
        if (summary["firstFailure"] is not JsonObject failure)
        {
            return $"── {Plural(steps, "step", "steps")}, all passed{script}";
        }

        string at = Location(failure["from"]) is { } location ? $" at {location}" : string.Empty;
        string notRun = steps > run ? $", {steps - run} not run" : string.Empty;
        return $"── failed at step {Number(failure["step"])} ({Text(failure["verb"])}, {Text(failure["code"])}){at}; " +
               $"{passed} of {steps} passed{notRun}{script}";
    }

    private static void AppendErrorDetail(StringBuilder text, JsonObject error)
    {
        if (Text(error["message"]) is { } message)
        {
            text.Append("\n    ").Append(message);
        }

        if (Text(error["hint"]) is { } hint)
        {
            text.Append("\n    hint: ").Append(hint);
        }

        AppendList(text, "closest", error["closest"]);
        AppendList(text, "elsewhere", error["elsewhere"]);
        AppendList(text, "candidates", error["candidates"]);
        if (error["matches"] is JsonArray matches && matches.Count > 0)
        {
            text.Append("\n    matches: ").Append(string.Join("; ", matches.Take(ListLimit)
                .OfType<JsonObject>()
                .Select(match => Text(match["scope"]) is { } scope
                    ? $"{Text(match["element"])} (--scope {scope})"
                    : Text(match["element"]))));
        }

        if (error["errors"] is JsonArray errors && errors.Count > 0)
        {
            foreach (JsonObject buildError in errors.OfType<JsonObject>().Take(ListLimit))
            {
                text.Append("\n    ").Append(Text(buildError["file"]));
                if (Number(buildError["line"]) is { } errorLine and > 0)
                {
                    text.Append('(').Append(errorLine).Append(',').Append(Number(buildError["column"])).Append(')');
                }

                text.Append(": ").Append(Text(buildError["code"])).Append(' ').Append(Text(buildError["message"]));
            }

            if (Number(error["errorCount"]) is { } count && count > Math.Min(errors.Count, ListLimit))
            {
                text.Append("\n    … ").Append(count - Math.Min(errors.Count, ListLimit)).Append(" more");
            }
        }

        if (error["findings"] is JsonArray findings)
        {
            // An audit's findings are its worklist, with no other text output, so every one is listed.
            foreach (JsonObject finding in findings.OfType<JsonObject>())
            {
                text.Append("\n    ").Append(DescribeFinding(finding));
            }
        }

        if (error["build"] is JsonObject build && Text(build["log"]) is { } log)
        {
            text.Append("\n    build log: ").Append(log);
        }
    }

    /// <summary>
    ///     One audit finding: its rule, the element (role, id or name), where it sits, and the message, e.g.
    ///     <c>missing-id [Button] "Save" in SetupPage &gt; Footer: …</c>.
    /// </summary>
    private static string DescribeFinding(JsonObject finding)
    {
        StringBuilder line = new();
        line.Append(Text(finding["rule"]) ?? "finding").Append(" [").Append(Text(finding["role"])).Append(']');
        if (Text(finding["id"]) is { Length: > 0 } id)
        {
            line.Append(" id: ").Append(id);
        }

        if (Text(finding["name"]) is { Length: > 0 } name)
        {
            line.Append(" \"").Append(name).Append('"');
        }

        if (finding["path"] is JsonArray { Count: > 0 } path)
        {
            line.Append(" in ").Append(string.Join(" > ", path.Select(Text)));
        }

        if (Number(finding["occurrences"]) is { } occurrences and > 1)
        {
            line.Append(" (×").Append(occurrences).Append(')');
        }

        if (Text(finding["scope"]) is { } scope)
        {
            line.Append(" --scope ").Append(scope);
        }

        if (Text(finding["message"]) is { } message)
        {
            line.Append(": ").Append(message);
        }

        return line.ToString();
    }

    private static void AppendList(StringBuilder text, string label, JsonNode? node)
    {
        if (node is not JsonArray items || items.Count == 0)
        {
            return;
        }

        IEnumerable<string?> shown = items.Take(ListLimit).Select(Text);
        string more = items.Count > ListLimit ? $", … {items.Count - ListLimit} more" : string.Empty;
        text.Append("\n    ").Append(label).Append(": ").Append(string.Join(", ", shown)).Append(more);
    }

    private static string DescribeWarning(string warning)
    {
        return warning == "stale-build"
            ? "stale-build: the running app is older than its build output; these results describe the old code. " +
              "Run rebuild, then repeat the check."
            : warning;
    }

    private static string DescribePause(JsonObject data)
    {
        string subject = Text(data["message"]) is { } message ? $"\"{message}\"" : "manual check";
        if (data["skipped"]?.GetValue<bool>() == true)
        {
            return $"{subject} skipped (not interactive)";
        }

        return Text(data["note"]) is { } note ? $"{subject} continued: {note}" : $"{subject} continued";
    }

    private static string DescribeTree(JsonObject data)
    {
        string nodes = $"{Plural(Number(data["nodes"]), "node", "nodes")} ({Number(data["walked"])} walked)";
        if (data["diff"]?.GetValue<bool>() != true)
        {
            return nodes;
        }

        if (data["baseline"]?.GetValue<bool>() == true)
        {
            return $"{nodes}, no earlier snapshot: baseline stored";
        }

        int added = (data["added"] as JsonArray)?.Count ?? 0;
        int removed = (data["removed"] as JsonArray)?.Count ?? 0;
        int changed = (data["changed"] as JsonArray)?.Count ?? 0;
        return added + removed + changed == 0
            ? "diff: no changes"
            : $"diff: {added} added, {removed} removed, {changed} changed";
    }

    private static string DescribeSession(JsonObject data)
    {
        string how = data["launched"]?.GetValue<bool>() == true ? "launched" : "attached";
        return $"pid {Number(data["pid"])} \"{Text(data["mainWindowTitle"])}\" ({how}, {Text(data["mode"])})";
    }

    private static string DescribeStatus(JsonObject data)
    {
        if (data["hasInstance"]?.GetValue<bool>() != true)
        {
            return $"no instance for {Text(data["target"])}";
        }

        string alive = data["alive"]?.GetValue<bool>() == true ? "running" : "not running";
        string stale = data["stale"]?.GetValue<bool>() == true ? ", stale build" : string.Empty;
        return $"pid {Number(data["pid"])} {alive} ({Text(data["mode"])}){stale}";
    }

    private static string DescribeRebuild(JsonObject data)
    {
        JsonObject? build = data["build"] as JsonObject;
        JsonObject? launch = data["launch"] as JsonObject;
        double seconds = (Number(build?["durationMs"]) ?? 0) / 1000.0;
        return $"{Text(data["target"])}: built {Text(build?["configuration"])} in " +
               $"{seconds.ToString("0.0", CultureInfo.InvariantCulture)} s, relaunched pid {Number(launch?["pid"])}";
    }

    private static string Awaited(JsonNode? node)
    {
        if (node is not JsonArray awaited || awaited.Count == 0)
        {
            return string.Empty;
        }

        return " → " + string.Join(", ", awaited.OfType<JsonObject>().Select(item =>
            Text(item["condition"]) == "gone" ? $"{Text(item["elementId"])} gone" : Text(item["elementId"])));
    }

    private static string? Conditions(JsonNode? node)
    {
        return node is JsonArray conditions && conditions.Count > 0
            ? string.Join(", ", conditions.Select(Text))
            : null;
    }

    private static string Scalars(JsonObject data)
    {
        return string.Join(' ', data
            .Where(pair => pair.Value is JsonValue)
            .Select(pair => $"{pair.Key}={pair.Value!.ToJsonString()}"));
    }

    /// <summary>The innermost <c>from</c> entry, with the script root stripped; null when the line has none.</summary>
    private static string? Location(JsonNode? node)
    {
        return node is JsonArray { Count: > 0 } from && Text(from[^1]) is { } innermost ? Shorten(innermost) : null;
    }

    private static string Shorten(string path)
    {
        return path.StartsWith(ScriptRootPrefix, StringComparison.OrdinalIgnoreCase)
            ? path[ScriptRootPrefix.Length..]
            : path;
    }

    private static string Join(params string?[] parts)
    {
        return string.Join(' ', parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static string Plural(long? count, string one, string many)
    {
        long value = count ?? 0;
        return $"{value} {(value == 1 ? one : many)}";
    }

    private static string? Text(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : node is JsonValue other && other.GetValueKind() != JsonValueKind.Null
                ? other.ToJsonString()
                : null;
    }

    private static long? Number(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetValue(out long whole) ? whole
            : value.TryGetValue(out int small) ? small
            : value.TryGetValue(out double real) ? (long)real
            : null;
    }
}