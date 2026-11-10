using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class PauseStepTests
{
    private sealed class FakePrompt(PauseAnswer answer) : IPausePrompt
    {
        public List<PauseRequest> Requests { get; } = [];

        public PauseAnswer Ask(PauseRequest request)
        {
            Requests.Add(request);
            return answer;
        }
    }

    private static CliArgs Step(params string[] options)
    {
        return new CliArgs(["pause", .. options]);
    }

    private static T Field<T>(object data, string name)
    {
        return (T)data.GetType().GetProperty(name)!.GetValue(data)!;
    }

    [Test]
    public void WithoutInteractive_PassesAtOnce_AsSkipped_WithoutAsking()
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.Failed, null, 0));

        object data = PauseStep.Run(Step("--message", "Check the chart"), false, prompt, 4, null);

        Assert.Multiple(() =>
        {
            Assert.That(prompt.Requests, Is.Empty);
            Assert.That(Field<bool>(data, "skipped"), Is.True);
            Assert.That(Field<string>(data, "message"), Is.EqualTo("Check the chart"));
        });
    }

    [Test]
    public void Continue_Passes_WithTheTrimmedNote_AndAsksWithTheStepsDetails()
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.Continued, "  Sign reads correctly \n", 1500));

        object data = PauseStep.Run(Step("--message", "Check the sign", "--timeout", "90"), true, prompt, 7,
            "lib/x.ui:3");

        Assert.Multiple(() =>
        {
            Assert.That(prompt.Requests, Is.EqualTo(new[]
            {
                new PauseRequest(7, "Check the sign", "lib/x.ui:3", TimeSpan.FromSeconds(90))
            }));
            Assert.That(Field<bool>(data, "skipped"), Is.False);
            Assert.That(Field<string>(data, "verdict"), Is.EqualTo("continued"));
            Assert.That(Field<string>(data, "note"), Is.EqualTo("Sign reads correctly"));
            Assert.That(Field<long>(data, "waitedMs"), Is.EqualTo(1500));
        });
    }

    [Test]
    public void Fail_IsManualCheckFailed_NamingTheCheckAndTheNote()
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.Failed, "Axis is flipped", 900));

        DriverException? ex = Assert.Throws<DriverException>(() =>
            PauseStep.Run(Step("--message", "Check the sign"), true, prompt, 2, null));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("manual-check-failed"));
            Assert.That(ex?.ExitCode, Is.EqualTo(PauseStep.FailedExitCode));
            Assert.That(ex?.Message, Is.EqualTo("The developer failed the manual check \"Check the sign\": Axis is flipped"));
            Assert.That(ex?.Details is not null && Field<string>(ex.Details, "note") == "Axis is flipped");
        });
    }

    [Test]
    public void Fail_WithoutANote_OrAMessage_StillReads()
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.Failed, "   ", 10));

        DriverException? ex = Assert.Throws<DriverException>(() => PauseStep.Run(Step(), true, prompt, 1, null));

        Assert.That(ex?.Message, Is.EqualTo("The developer failed the manual check."));
    }

    [Test]
    public void NoAnswerWithinTheTimeout_IsPauseTimeout()
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.TimedOut, null, 30000));

        DriverException? ex = Assert.Throws<DriverException>(() =>
            PauseStep.Run(Step("--timeout", "30"), true, prompt, 3, null));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("pause-timeout"));
            Assert.That(ex?.Message, Is.EqualTo("Nobody answered the manual check within 30s."));
        });
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void NonPositiveTimeout_IsBadArguments(string timeout)
    {
        FakePrompt prompt = new(new PauseAnswer(PauseVerdict.Continued, null, 0));

        Assert.That(Assert.Throws<DriverException>(() =>
            PauseStep.Run(Step("--timeout", timeout), true, prompt, 1, null))?.Code, Is.EqualTo("bad-arguments"));
    }
}