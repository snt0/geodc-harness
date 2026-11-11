using System.IO;
using System.Text.Json;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
[NonParallelizable]
public sealed class JsonOutputTests
{
    [TearDown]
    public void ClearWarning()
    {
        JsonOutput.WarnFrom(null);
        JsonOutput.SetStepOrigin(null);
        JsonOutput.EndStep();
        JsonOutput.Format = DriverOutputFormat.Json;
        DoReport.Active = null;
    }

    private static string[] CaptureLines(Action write)
    {
        TextWriter original = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter captured = new();
        using StringWriter error = new();
        Console.SetOut(captured);
        Console.SetError(error);
        try
        {
            write();
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }

        return captured.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? WarningOf(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("warning", out JsonElement warning) ? warning.GetString() : null;
    }

    private static JsonElement CaptureFailureError(ElementLookupHints? lookup)
    {
        TextWriter original = Console.Out;
        using StringWriter captured = new();
        Console.SetOut(captured);
        try
        {
            JsonOutput.Failure("click", "element-not-found", "No element.", ["SaveButton (Button)"], null, null,
                lookup);
        }
        finally
        {
            Console.SetOut(original);
        }

        using JsonDocument document = JsonDocument.Parse(captured.ToString());
        return document.RootElement.GetProperty("error").Clone();
    }

    [Test]
    public void Failure_WithLookup_AddsItsNonEmptyFields_BesideTheExistingOnes()
    {
        ElementLookupHints lookup = new(["SaveButton"], [], [new ScopedMatch("SaveButton (Button)", "CommandBarPanel")],
            "use --id SaveButton");

        JsonElement error = CaptureFailureError(lookup);

        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("element-not-found"));
            Assert.That(error.GetProperty("candidates")[0].GetString(), Is.EqualTo("SaveButton (Button)"));
            Assert.That(error.GetProperty("closest")[0].GetString(), Is.EqualTo("SaveButton"));
            Assert.That(error.TryGetProperty("elsewhere", out _), Is.False);
            Assert.That(error.GetProperty("matches")[0].GetProperty("element").GetString(),
                Is.EqualTo("SaveButton (Button)"));
            Assert.That(error.GetProperty("matches")[0].GetProperty("scope").GetString(),
                Is.EqualTo("CommandBarPanel"));
            Assert.That(error.GetProperty("hint").GetString(), Is.EqualTo("use --id SaveButton"));
        });
    }

    [Test]
    public void Failure_WithoutLookup_KeepsItsShape()
    {
        JsonElement error = CaptureFailureError(null);

        Assert.That(error.EnumerateObject().Select(property => property.Name),
            Is.EqualTo(new[] { "code", "message", "candidates" }));
    }

    [Test]
    public void Every_kind_of_line_ends_with_the_warning_while_there_is_one()
    {
        JsonOutput.WarnFrom(() => BuildStaleness.Warning);

        string[] lines = CaptureLines(() =>
        {
            JsonOutput.Success("tree", new { nodes = 3 });
            JsonOutput.Success("click", new { elementId = "SaveButton" }, 1);
            JsonOutput.Failure("click", "element-not-found", "No element.", step: 2);
            JsonOutput.DoSummary(2, 2, 1, null, new { step = 2 });
        });

        using JsonDocument first = JsonDocument.Parse(lines[0]);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(4));
            Assert.That(lines.Select(WarningOf), Is.All.EqualTo("stale-build"));
            Assert.That(first.RootElement.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "ok", "verb", "data", "warning" }));
        });
    }

    [Test]
    public void A_step_origin_adds_from_before_the_warning_until_it_is_cleared()
    {
        JsonOutput.WarnFrom(() => "stale-build");

        string[] lines = CaptureLines(() =>
        {
            JsonOutput.SetStepOrigin(["dev/ui-scripts/top.ui:3", "dev/ui-scripts/lib/goto.ui:2"]);
            JsonOutput.Success("click", new { id = "A" }, 1);
            JsonOutput.SetStepOrigin([]);
            JsonOutput.Success("click", new { id = "B" }, 2);
            JsonOutput.SetStepOrigin(null);
            JsonOutput.DoSummary(2, 2, 2, null, null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.EndWith(
                "\"from\":[\"dev/ui-scripts/top.ui:3\",\"dev/ui-scripts/lib/goto.ui:2\"]," +
                "\"warning\":\"stale-build\"}"));
            Assert.That(lines[1], Does.Not.Contain("\"from\""));
            Assert.That(lines[2], Does.Not.Contain("\"from\""));
        });
    }

    [Test]
    public void Text_format_prints_rendered_lines_with_the_warning_once()
    {
        JsonOutput.WarnFrom(() => "stale-build");
        JsonOutput.Format = DriverOutputFormat.Text;

        string[] lines = CaptureLines(() =>
        {
            JsonOutput.Success("click", new { elementId = "A" }, 1);
            JsonOutput.Failure("click", "element-not-found", "No element 'B'.", null, 2);
            JsonOutput.DoSummary(2, 2, 1, null, new { step = 2, verb = "click", code = "element-not-found" });
        });

        Assert.That(lines, Has.Length.EqualTo(5), "each part of a line ends with the platform's line ending");
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.StartWith("! stale-build:"));
            Assert.That(lines[1], Is.EqualTo("✓  1 click A"));
            Assert.That(lines[2], Is.EqualTo("✗  2 click: element-not-found"));
            Assert.That(lines[3], Is.EqualTo("    No element 'B'."));
            Assert.That(lines[4], Is.EqualTo("── failed at step 2 (click, element-not-found); 1 of 2 passed"));
        });
    }

    [Test]
    public void A_report_records_each_line_without_changing_the_json()
    {
        DoInvocation invocation = DoInvocation.Parse(["click", "--id", "A"], _ => throw new FileNotFoundException());
        JsonOutput.SetStepOrigin(["dev/ui-scripts/x.ui:1"]);
        string[] plain = CaptureLines(() => JsonOutput.Success("click", new { elementId = "A" }, 1));
        DoReport report = new("run.md", invocation, DateTime.UnixEpoch);
        DoReport.Active = report;

        string[] reported = CaptureLines(() =>
        {
            JsonOutput.BeginStep();
            JsonOutput.Success("click", new { elementId = "A" }, 1);
            JsonOutput.SetStepOrigin(null);
            JsonOutput.EndStep();
            JsonOutput.DoSummary(1, 1, 1, null, null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(reported[0], Is.EqualTo(plain[0]));
            Assert.That(reported[1], Does.EndWith(",\"report\":\"run.md\"}}"));
            Assert.That(report.Render("/", path => path), Does.Contain("| 1 | `click --id A` | ✓ A |")
                .And.Contain("| Result | ✓ Passed: 1 of 1 steps |"));
        });
    }

    [Test]
    public void Without_a_warning_lines_keep_their_shape()
    {
        JsonOutput.WarnFrom(() => null);

        string[] lines = CaptureLines(() => JsonOutput.Success("status", new { hasInstance = false }));

        Assert.That(lines.Single(), Is.EqualTo("{\"ok\":true,\"verb\":\"status\",\"data\":{\"hasInstance\":false}}"));
    }

    [Test]
    public void The_warning_source_is_asked_once_when_the_first_line_is_written()
    {
        int calls = 0;
        JsonOutput.WarnFrom(() =>
        {
            calls++;
            return BuildStaleness.Warning;
        });
        int callsBeforeOutput = calls;

        CaptureLines(() =>
        {
            JsonOutput.Success("click", new { }, 1);
            JsonOutput.Success("click", new { }, 2);
        });

        Assert.Multiple(() =>
        {
            Assert.That(callsBeforeOutput, Is.Zero);
            Assert.That(calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_warning_source_that_throws_loses_the_warning_but_not_the_line()
    {
        JsonOutput.WarnFrom(() => throw new IOException("instance file unreadable"));

        string[] lines = CaptureLines(() => JsonOutput.Success("tree", new { nodes = 1 }));

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(WarningOf(lines[0]), Is.Null);
        });
    }

    [Test]
    public void Failure_details_are_added_to_the_error_after_its_standard_fields()
    {
        string[] lines = CaptureLines(() => JsonOutput.Failure("rebuild", "build-failed", "Build failed.",
            details: new { errorCount = 2, killedPid = (int?)null }));

        using JsonDocument document = JsonDocument.Parse(lines.Single());
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Multiple(() =>
        {
            Assert.That(error.EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "code", "message", "errorCount", "killedPid" }));
            Assert.That(error.GetProperty("errorCount").GetInt32(), Is.EqualTo(2));
        });
    }
}