using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class ScreenshotOptionsTests
{
    private static ScreenshotOptions Parse(params string[] options)
    {
        return ScreenshotOptions.Parse(new CliArgs(["screenshot", .. options]));
    }

    [Test]
    public void NoOptions_UsesDefaultMaxWidthAndNoPaddingOrHighlights()
    {
        ScreenshotOptions options = Parse();

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxWidth, Is.EqualTo(ScreenshotOptions.DefaultMaxWidth));
            Assert.That(options.PadDips, Is.Zero);
            Assert.That(options.HighlightIds, Is.Empty);
        });
    }

    [Test]
    public void MaxWidthZero_DisablesTheLimit()
    {
        Assert.That(Parse("--max-width", "0").MaxWidth, Is.Zero);
    }

    [TestCase("-1")]
    [TestCase("wide")]
    public void MaxWidth_RejectsNegativeOrNonInteger(string value)
    {
        DriverException? ex = Assert.Throws<DriverException>(() => Parse("--max-width", value));

        Assert.That(ex?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void Pad_WithId_IsAccepted()
    {
        Assert.That(Parse("--id", "SaveButton", "--pad", "12").PadDips, Is.EqualTo(12));
    }

    [Test]
    public void Pad_WithoutId_IsRejected()
    {
        DriverException? ex = Assert.Throws<DriverException>(() => Parse("--region", "0,0,10,10", "--pad", "8"));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(ex?.Message, Does.Contain("--pad"));
        });
    }

    [Test]
    public void Pad_Negative_IsRejected()
    {
        DriverException? ex = Assert.Throws<DriverException>(() => Parse("--id", "SaveButton", "--pad", "-4"));

        Assert.That(ex?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void Highlight_SplitsTrimsAndDropsDuplicates()
    {
        ScreenshotOptions options = Parse("--highlight", "SaveButton, NameInput,,SaveButton");

        Assert.That(options.HighlightIds, Is.EqualTo(new[] { "SaveButton", "NameInput" }));
    }

    [Test]
    public void Highlight_WithNoIds_IsRejected()
    {
        DriverException? ex = Assert.Throws<DriverException>(() => Parse("--highlight", " , "));

        Assert.That(ex?.Code, Is.EqualTo("bad-arguments"));
    }
}