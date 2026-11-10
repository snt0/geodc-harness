using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class AwaitPlanTests
{
    private static AwaitPlan Parse(params string[] options)
    {
        return AwaitPlan.Parse(new CliArgs(["click", "--id", "OpenButton", .. options]));
    }

    [Test]
    public void NoAwaitOptions_IsEmpty()
    {
        Assert.That(Parse().IsEmpty, Is.True);
    }

    [Test]
    public void AwaitGone_RunsBeforeAwait()
    {
        AwaitPlan plan = Parse("--await", "NextPanel", "--await-gone", "PopupPanel");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Requests.Select(request => request.ElementId),
                Is.EqualTo(new[] { "PopupPanel", "NextPanel" }));
            Assert.That(plan.Requests[0].Conditions.IsGone, Is.True);
            Assert.That(plan.Requests[1].Conditions.IsReady, Is.True);
        });
    }

    [Test]
    public void AwaitTimeout_DefaultsAndParses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Parse("--await", "X").Timeout,
                Is.EqualTo(TimeSpan.FromSeconds(AwaitPlan.DefaultTimeoutSeconds)));
            Assert.That(Parse("--await", "X", "--await-timeout", "12.5").Timeout,
                Is.EqualTo(TimeSpan.FromSeconds(12.5)));
        });
    }

    [Test]
    public void AwaitTimeout_WithoutTarget_IsBadArguments()
    {
        DriverException? failure = Assert.Throws<DriverException>(() => Parse("--await-timeout", "3"));
        Assert.That(failure?.Code, Is.EqualTo("bad-arguments"));
    }

    [TestCase("abc")]
    [TestCase("-1")]
    public void AwaitTimeout_RejectsNonSeconds(string raw)
    {
        DriverException? failure = Assert.Throws<DriverException>(() => Parse("--await", "X", "--await-timeout", raw));
        Assert.That(failure?.Code, Is.EqualTo("bad-arguments"));
    }
}