using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class DriverOutputOptionsTests
{
    private static string? FailureCode(params string[] args)
    {
        return Assert.Throws<DriverException>(() => DriverOutputOptions.Extract(args))?.Code;
    }

    [Test]
    public void Defaults_ToJsonWithoutAReport_AndLeavesTheArgumentsAlone()
    {
        (string[] args, DriverOutputOptions options) = DriverOutputOptions.Extract(["click", "--id", "A"]);

        Assert.Multiple(() =>
        {
            Assert.That(args, Is.EqualTo(new[] { "click", "--id", "A" }));
            Assert.That(options, Is.EqualTo(new DriverOutputOptions(DriverOutputFormat.Json, null)));
        });
    }

    [TestCase(true, DriverOutputFormat.Json)]
    [TestCase(false, DriverOutputFormat.Text)]
    public void DefaultFormat_IsTextAtATerminal_AndJsonWhenPiped(bool redirected, DriverOutputFormat expected)
    {
        Assert.That(DriverOutputOptions.DefaultFormatFor(redirected), Is.EqualTo(expected));
    }

    [Test]
    public void DefaultFormat_AppliesOnlyWhenFormatIsNotGiven()
    {
        (_, DriverOutputOptions bare) =
            DriverOutputOptions.Extract(["tree"], defaultFormat: DriverOutputFormat.Text);
        (_, DriverOutputOptions json) =
            DriverOutputOptions.Extract(["tree", "--format", "json"], defaultFormat: DriverOutputFormat.Text);
        (_, DriverOutputOptions empty) = DriverOutputOptions.Extract([], defaultFormat: DriverOutputFormat.Text);

        Assert.Multiple(() =>
        {
            Assert.That(bare.Format, Is.EqualTo(DriverOutputFormat.Text));
            Assert.That(json.Format, Is.EqualTo(DriverOutputFormat.Json));
            Assert.That(empty.Format, Is.EqualTo(DriverOutputFormat.Text));
        });
    }

    [Test]
    public void Do_TakesFormatAndReport_FromItsLeadingOptionsOnly()
    {
        (string[] args, DriverOutputOptions options) = DriverOutputOptions.Extract(
        [
            "do", "--target", "sample", "--format", "TEXT", "--file", "x.ui", "--report", "out/run.md",
            "--var", "a=1"
        ]);
        (string[] inline, DriverOutputOptions stepOptions) = DriverOutputOptions.Extract(
            ["do", "settext", "--id", "A", "--value", "--format", "++", "assert", "--id", "B", "--format", "text"]);

        Assert.Multiple(() =>
        {
            Assert.That(args, Is.EqualTo(new[] { "do", "--target", "sample", "--file", "x.ui", "--var", "a=1" }));
            Assert.That(options, Is.EqualTo(new DriverOutputOptions(DriverOutputFormat.Text, "out/run.md")));
            Assert.That(inline, Has.Length.EqualTo(12), "a step's arguments are never taken as output options");
            Assert.That(stepOptions.Format, Is.EqualTo(DriverOutputFormat.Json));
        });
    }

    [Test]
    public void OtherVerbs_TakeFormatAnywhereAmongTheirOptions_SkippingFlagsAndValues()
    {
        (string[] args, DriverOutputOptions options) = DriverOutputOptions.Extract(
            ["tree", "--interactable", "--format", "text", "--scope", "--format"]);
        (string[] rebuild, DriverOutputOptions rebuildOptions) = DriverOutputOptions.Extract(
            ["rebuild", "--then", "lib/x", "--report", "r.md", "--passive"]);

        Assert.Multiple(() =>
        {
            Assert.That(args, Is.EqualTo(new[] { "tree", "--interactable", "--scope", "--format" }));
            Assert.That(options.Format, Is.EqualTo(DriverOutputFormat.Text));
            Assert.That(rebuild, Is.EqualTo(new[] { "rebuild", "--then", "lib/x", "--passive" }));
            Assert.That(rebuildOptions.ReportPath, Is.EqualTo("r.md"));
        });
    }

    [Test]
    public void Append_IsTakenForAnyVerb_AndFallsBackToTheEnvironment()
    {
        (string[] args, DriverOutputOptions options) = DriverOutputOptions.Extract(
            ["do", "--append", ".ui-driver/run.log", "--file", "x.ui"], ".ui-driver/env.log");
        (_, DriverOutputOptions fromEnvironment) = DriverOutputOptions.Extract(["status"], ".ui-driver/env.log");
        (_, DriverOutputOptions blankEnvironment) = DriverOutputOptions.Extract(["status"], "  ");
        (string[] tree, DriverOutputOptions treeOptions) = DriverOutputOptions.Extract(
            ["tree", "--interactable", "--append", "t.log"]);

        Assert.Multiple(() =>
        {
            Assert.That(args, Is.EqualTo(new[] { "do", "--file", "x.ui" }));
            Assert.That(options.AppendPath, Is.EqualTo(".ui-driver/run.log"), "--append wins over the environment");
            Assert.That(fromEnvironment.AppendPath, Is.EqualTo(".ui-driver/env.log"));
            Assert.That(blankEnvironment.AppendPath, Is.Null);
            Assert.That(tree, Is.EqualTo(new[] { "tree", "--interactable" }));
            Assert.That(treeOptions.AppendPath, Is.EqualTo("t.log"));
        });
    }

    [Test]
    public void BadValues_AndReportOutsideDoOrRebuild_AreBadArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode("tree", "--format", "yaml"), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode("tree", "--format"), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode("tree", "--report", "r.md"), Is.EqualTo("bad-arguments"));
        });
    }
}