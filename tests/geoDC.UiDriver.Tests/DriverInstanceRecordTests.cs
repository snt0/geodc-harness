using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class DriverInstanceRecordTests
{
    [Test]
    public void An_instance_round_trips_with_its_launch_arguments()
    {
        DriverInstanceRecord instance = new("app", 4242, new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc),
            @"C:\src\geodc\src\geoDC.App\bin\Release\net10.0-windows10.0.17763.0\geoDC.App.exe", true,
            @"C:\src\geodc\dev\sample-projects\sample.gdcproj", @"C:\src\geodc");

        Assert.That(DriverInstanceRecord.Parse(instance.ToJson()), Is.EqualTo(instance));
    }

    [Test]
    public void An_instance_without_launch_arguments_reads_with_their_defaults()
    {
        DriverInstanceRecord? instance = DriverInstanceRecord.Parse(
            """{"target":"sample","pid":17,"startTimeUtc":"2026-10-16T09:00:00Z","executablePath":"C:\\s.exe"}""");

        Assert.Multiple(() =>
        {
            Assert.That(instance?.Pid, Is.EqualTo(17));
            Assert.That(instance?.Passive, Is.False);
            Assert.That(instance?.ProjectPath, Is.Null);
            Assert.That(instance?.WorkingDirectory, Is.Null);
        });
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("""{"pid":17,"startTimeUtc":"2026-10-16T09:00:00Z","executablePath":"C:\\s.exe"}""")]
    [TestCase("""{"target":"app","pid":0,"startTimeUtc":"2026-10-16T09:00:00Z","executablePath":"C:\\s.exe"}""")]
    [TestCase("""{"target":"app","pid":17,"startTimeUtc":"2026-10-16T09:00:00Z"}""")]
    public void Content_that_is_not_an_instance_record_reads_as_none(string json)
    {
        Assert.That(DriverInstanceRecord.Parse(json), Is.Null);
    }
}