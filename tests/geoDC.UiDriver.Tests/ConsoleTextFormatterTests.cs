using System.Text.Json.Nodes;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class ConsoleTextFormatterTests
{
    private static string Render(string json, long? elapsedMs = null, bool includeWarning = true)
    {
        return ConsoleTextFormatter.Format((JsonObject)JsonNode.Parse(json)!, elapsedMs, includeWarning);
    }

    [Test]
    public void StepSuccess_ShowsTheStep_WhatWasAwaited_TheTime_AndWhereItCameFrom()
    {
        string text = Render("""
            {"ok":true,"verb":"click","step":2,"data":{"elementId":"CalibrationTab","method":"invoke",
             "awaited":[{"elementId":"AttemptList","condition":"ready","metOnFirstLook":false,"waitedMs":180},
                        {"elementId":"PopupPanel","condition":"gone","metOnFirstLook":true,"waitedMs":0}]},
             "from":["dev/ui-scripts/app-open-calibration.ui:13","dev/ui-scripts/lib/app-goto-page.ui:12"]}
            """, 212);

        Assert.That(text, Is.EqualTo(
            "✓  2 click CalibrationTab → AttemptList, PopupPanel gone (212 ms)  · lib/app-goto-page.ui:12"));
    }

    [TestCase("""{"ok":true,"verb":"settext","data":{"elementId":"NameInput","method":"value","value":"Roll"}}""",
        "✓ settext NameInput = \"Roll\"")]
    [TestCase("""{"ok":true,"verb":"select","data":{"containerId":"AttemptList","item":"Pitch (INITIAL)"}}""",
        "✓ select \"Pitch (INITIAL)\" in AttemptList")]
    [TestCase("""{"ok":true,"verb":"assert","data":{"elementId":"SaveButton","conditions":["exists","disabled"]}}""",
        "✓ assert SaveButton exists, disabled")]
    [TestCase("""{"ok":true,"verb":"screenshot","data":{"path":".ui-driver/screenshots/a.png","width":800,"height":600,"elementId":"Page"}}""",
        "✓ screenshot .ui-driver/screenshots/a.png (800x600) of Page")]
    [TestCase("""{"ok":true,"verb":"tree","data":{"nodes":12,"walked":40,"diff":true,"baseline":false,"added":[{}],"removed":[],"changed":[{},{}]}}""",
        "✓ tree diff: 1 added, 0 removed, 2 changed")]
    [TestCase("""{"ok":true,"verb":"tree","data":{"nodes":1,"walked":3}}""", "✓ tree 1 node (3 walked)")]
    [TestCase("""{"ok":true,"verb":"find","data":{"matches":1,"walked":7}}""", "✓ find 1 match")]
    [TestCase("""{"ok":true,"verb":"source","data":{"texts":4,"sourced":3,"ambiguous":1,"unsourced":1,"walked":30}}""",
        "✓ source 4 texts, 3 from the strings files (1 ambiguous), 1 not")]
    [TestCase("""{"ok":true,"verb":"source","data":{"texts":0,"sourced":0,"ambiguous":0,"unsourced":0,"walked":3}}""",
        "✓ source no text")]
    [TestCase("""{"ok":true,"verb":"attach","data":{"pid":42,"mainWindowTitle":"geoDC","launched":true,"mode":"passive"}}""",
        "✓ attach pid 42 \"geoDC\" (launched, passive)")]
    [TestCase("""{"ok":true,"verb":"status","data":{"target":"app","hasInstance":false}}""", "✓ status no instance for app")]
    [TestCase("""{"ok":true,"verb":"status","data":{"target":"app","hasInstance":true,"pid":7,"alive":true,"mode":"active","stale":true}}""",
        "✓ status pid 7 running (active), stale build")]
    [TestCase("""{"ok":true,"verb":"rebuild","data":{"target":"app","build":{"configuration":"Debug","durationMs":21873},"launch":{"pid":5120}}}""",
        "✓ rebuild app: built Debug in 21.9 s, relaunched pid 5120")]
    [TestCase("""{"ok":true,"verb":"kill","data":{"wasRunning":false,"target":"sample"}}""",
        "✓ kill nothing was running for sample")]
    [TestCase("""{"ok":true,"verb":"pause","data":{"message":"Check the chart","skipped":true,"verdict":null,"note":null}}""",
        "✓ pause \"Check the chart\" skipped (not interactive)")]
    [TestCase("""{"ok":true,"verb":"pause","data":{"message":null,"skipped":false,"verdict":"continued","note":"Looks right"}}""",
        "✓ pause manual check continued: Looks right")]
    [TestCase("""{"ok":true,"verb":"future","data":{"answer":42,"name":"x","nested":{"a":1}}}""",
        "✓ future answer=42 name=\"x\"")]
    public void Success_DescribesTheVerbsData(string json, string expected)
    {
        Assert.That(Render(json), Is.EqualTo(expected));
    }

    [Test]
    public void Failure_ShowsCodeMessageHintAndLookups_OnIndentedLines()
    {
        string text = Render("""
            {"ok":false,"verb":"click","step":3,"error":{"code":"element-not-found","message":"No element 'SaveButon'.",
             "candidates":["SaveButton (Button)"],"closest":["SaveButton","SaveAsButton"],
             "elsewhere":["title:Approve attempt"],"hint":"use --id SaveButton"},
             "from":["dev/ui-scripts/x.ui:4"]}
            """, 5003);

        Assert.That(text, Is.EqualTo(string.Join('\n',
            "✗  3 click: element-not-found (5003 ms)  · x.ui:4",
            "    No element 'SaveButon'.",
            "    hint: use --id SaveButton",
            "    closest: SaveButton, SaveAsButton",
            "    elsewhere: title:Approve attempt",
            "    candidates: SaveButton (Button)")));
    }

    [Test]
    public void BuildFailure_ListsItsErrorsAndLog()
    {
        string text = Render("""
            {"ok":false,"verb":"rebuild","error":{"code":"build-failed","message":"dotnet build failed.",
             "errors":[{"file":"C:\\src\\A.cs","line":12,"column":5,"code":"CS1002","message":"; expected"},
                       {"file":"MSBUILD","line":0,"column":0,"code":"MSB1009","message":"Project file does not exist."}],
             "errorCount":7,"build":{"log":".ui-driver/builds/app.log"}}}
            """);

        Assert.That(text, Is.EqualTo(string.Join('\n',
            "✗ rebuild: build-failed",
            "    dotnet build failed.",
            "    C:\\src\\A.cs(12,5): CS1002 ; expected",
            "    MSBUILD: MSB1009 Project file does not exist.",
            "    … 5 more",
            "    build log: .ui-driver/builds/app.log")));
    }

    [Test]
    public void AuditFailure_ListsEveryFinding()
    {
        string text = Render("""
            {"ok":false,"verb":"audit","error":{"code":"audit-findings",
             "message":"2 automation convention finding(s): 1 missing-id, 1 duplicate-id.","scope":null,"walked":90,
             "count":2,"findings":[
              {"rule":"missing-id","role":"Button","id":null,"name":"Save","path":["SetupPage","Footer"],
               "scope":"Footer","occurrences":1,"message":"Interactable Button has no AutomationId."},
              {"rule":"duplicate-id","role":"Edit","id":"NameInput","name":"","path":[],"scope":null,
               "occurrences":2,"message":"NameInput is used 2 times."}]}}
            """);

        Assert.That(text, Is.EqualTo(string.Join('\n',
            "✗ audit: audit-findings",
            "    2 automation convention finding(s): 1 missing-id, 1 duplicate-id.",
            "    missing-id [Button] \"Save\" in SetupPage > Footer --scope Footer: Interactable Button has no AutomationId.",
            "    duplicate-id [Edit] id: NameInput (×2): NameInput is used 2 times.")));
    }

    [Test]
    public void Warning_IsShownAboveTheLine_OnlyWhenAsked()
    {
        const string json = """{"ok":true,"verb":"settle","step":1,"data":{"ms":300},"warning":"stale-build"}""";

        Assert.Multiple(() =>
        {
            Assert.That(Render(json), Does.StartWith("! stale-build: the running app is older than its build output")
                .And.EndWith("\n✓  1 settle 300 ms"));
            Assert.That(Render(json, includeWarning: false), Is.EqualTo("✓  1 settle 300 ms"));
        });
    }

    [Test]
    public void Summary_PassedAndFailed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Render("""
                    {"ok":true,"verb":"do","summary":{"steps":6,"run":6,"passed":6,
                     "script":"dev/ui-scripts/app-open-calibration.ui","firstFailure":null}}
                    """),
                Is.EqualTo("── 6 steps, all passed (app-open-calibration.ui)"));
            Assert.That(Render("""
                    {"ok":false,"verb":"do","summary":{"steps":10,"run":5,"passed":4,"script":null,
                     "firstFailure":{"step":5,"verb":"select","code":"item-not-found",
                      "from":["dev/ui-scripts/lib/app-open-attempt.ui:17"]}}}
                    """),
                Is.EqualTo("── failed at step 5 (select, item-not-found) at lib/app-open-attempt.ui:17; " +
                           "4 of 10 passed, 5 not run"));
        });
    }
}