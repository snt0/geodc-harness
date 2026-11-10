using System.Text.Json.Nodes;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class DoReportTests
{
    private static readonly DateTime Started = new(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);

    private static DoInvocation Invocation()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/check.ui"] =
                "@target app\n@var attempt=Pitch calibration (INITIAL)\nrun lib/open attempt='${attempt}'\n" +
                "screenshot --id DataInputPanel\nassert --id Done",
            ["dev/ui-scripts/lib/open.ui"] = "select --id AttemptList --item '${attempt}'"
        };
        return DoInvocation.Parse(["--file", "dev/ui-scripts/check.ui"], path => files[path]);
    }

    private static void Record(DoReport report, string json, long? elapsedMs = null)
    {
        report.Record((JsonObject)JsonNode.Parse(json)!, elapsedMs);
    }

    private static string Render(DoReport report)
    {
        return report.Render("/work/out", path => path.StartsWith('/') ? path : "/work/" + path);
    }

    [Test]
    public void PassedRun_HasTheScriptVariables_AStepTable_AndEmbeddedScreenshots()
    {
        DoReport report = new("out/run.md", Invocation(), Started);
        Record(report, """
            {"ok":true,"verb":"select","step":1,"data":{"containerId":"AttemptList","item":"Pitch calibration (INITIAL)"},
             "from":["dev/ui-scripts/check.ui:3","dev/ui-scripts/lib/open.ui:1"]}
            """, 120);
        Record(report, """
            {"ok":true,"verb":"screenshot","step":2,"data":{"path":".ui-driver/screenshots/shot 1.png","width":800,
             "height":600,"elementId":"DataInputPanel"},"from":["dev/ui-scripts/check.ui:4"]}
            """, 340);
        Record(report, """{"ok":true,"verb":"assert","step":3,"data":{"elementId":"Done","conditions":[]},"from":["dev/ui-scripts/check.ui:5"]}""", 3);
        Record(report, """{"ok":true,"verb":"do","summary":{"steps":3,"run":3,"passed":3,"script":"dev/ui-scripts/check.ui","firstFailure":null}}""");

        Assert.That(Render(report), Is.EqualTo("""
            # UI driver run: check.ui

            | | |
            |---|---|
            | Result | ✓ Passed: 3 of 3 steps |
            | Target | app |
            | Script | `dev/ui-scripts/check.ui` |
            | Variables | `attempt` = Pitch calibration (INITIAL) |
            | Started | 2026-10-31 16:00:00 UTC |

            ## Steps

            | # | Step | Result | Time | From |
            |---|---|---|---|---|
            | 1 | `select --id AttemptList --item 'Pitch calibration (INITIAL)'` | ✓ "Pitch calibration (INITIAL)" in AttemptList | 120 ms | `lib/open.ui:1` |
            | 2 | `screenshot --id DataInputPanel` | ✓ .ui-driver/screenshots/shot 1.png (800x600) of DataInputPanel | 340 ms | `check.ui:4` |
            | 3 | `assert --id Done` | ✓ Done | 3 ms | `check.ui:5` |

            ## Screenshots

            ### Step 2: DataInputPanel

            ![Step 2 screenshot](../.ui-driver/screenshots/shot%201.png)

            """.ReplaceLineEndings("\n")));
    }

    [Test]
    public void FailedRun_StopsAtTheFailingStep_AndDescribesTheFailure()
    {
        DoReport report = new("run.md", Invocation(), Started);
        Record(report, """
            {"ok":false,"verb":"select","step":1,"error":{"code":"item-not-found","message":"No item 'X' in AttemptList.",
             "closest":["Pitch calibration (INITIAL)"],"hint":"check the | name"},
             "from":["dev/ui-scripts/check.ui:3","dev/ui-scripts/lib/open.ui:1"]}
            """, 5000);
        Record(report, """
            {"ok":false,"verb":"do","summary":{"steps":3,"run":1,"passed":0,"script":"dev/ui-scripts/check.ui",
             "firstFailure":{"step":1,"verb":"select","code":"item-not-found"}}}
            """);

        string markdown = Render(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("| Result | ✗ Failed at step 1: 0 of 3 passed, 2 not run |"));
            Assert.That(markdown, Does.Contain("| ✗ item-not-found | 5000 ms | `lib/open.ui:1` |"));
            Assert.That(markdown, Does.Contain("""
                ## Failure

                **Step 1, `select`: item-not-found**

                No item 'X' in AttemptList.

                - From: `lib/open.ui:1` < `check.ui:3`
                - Hint: check the | name
                - Closest: `Pitch calibration (INITIAL)`
                """.ReplaceLineEndings("\n")));
            Assert.That(markdown, Does.Not.Contain("## Screenshots"));
            Assert.That(markdown, Does.Not.Contain("| 2 |"), "steps that did not run have no row");
        });
    }

    [Test]
    public void StaleBuildWarning_IsShownOnce_InTheHeader()
    {
        DoReport report = new("run.md", Invocation(), Started);
        Record(report, """{"ok":true,"verb":"assert","step":1,"data":{"elementId":"Done"},"warning":"stale-build"}""", 2);
        Record(report, """{"ok":true,"verb":"do","summary":{"steps":1,"run":1,"passed":1,"script":null,"firstFailure":null},"warning":"stale-build"}""");

        string markdown = Render(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("| Result | ✓ Passed: 1 of 1 steps |\n| Warning | ⚠ stale-build: " +
                "the app was older than its build output, so this run checked the old code. |\n"));
            Assert.That(markdown.Split("stale-build").Length - 1, Is.EqualTo(1));
        });
    }

    [Test]
    public void RunThatCouldNotStart_ReportsTheFailure_WithNoSteps()
    {
        DoReport report = new("run.md", Invocation(), Started);
        Record(report, """{"ok":false,"verb":"do","error":{"code":"no-instance","message":"No geoDC instance is running."}}""");

        string markdown = Render(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("| Result | ✗ Failed before the first step |"));
            Assert.That(markdown, Does.Contain("No step ran."));
            Assert.That(markdown, Does.Contain("**`do`: no-instance**"));
        });
    }

    [Test]
    public void InlineRun_IsTitledAsInlineSteps_WithoutScriptOrVariables()
    {
        DoInvocation invocation = DoInvocation.Parse(["click", "--id", "A|B"], _ => throw new FileNotFoundException());
        DoReport report = new("run.md", invocation, Started);
        Record(report, """{"ok":true,"verb":"click","step":1,"data":{"elementId":"A|B"}}""", 10);

        string markdown = Render(report);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.StartWith("# UI driver run: inline steps\n"));
            Assert.That(markdown, Does.Not.Contain("| Script |").And.Not.Contain("| Variables |"));
            Assert.That(markdown, Does.Contain("| 1 | `click --id A\\|B` | ✓ A\\|B | 10 ms |  |"));
            Assert.That(markdown, Does.Contain("| Result | Did not finish |"));
        });
    }

    [Test]
    public void Save_WritesTheFile_CreatingItsFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ui-driver-report-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "nested", "run.md");
        try
        {
            DoReport report = new(path, Invocation(), Started);
            report.Save();

            Assert.That(File.ReadAllText(path), Does.StartWith("# UI driver run: check.ui"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}