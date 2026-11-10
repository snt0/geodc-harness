using System.IO;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     A <c>sample create</c> run leaves its diagnostic record in <c>.ui-driver/sample-projects/</c>, since the
///     driver's stdout carries only the one-line result.
/// </summary>
[TestFixture]
public sealed class SampleProjectCreateLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 1, 2, 3, TimeSpan.Zero);

    private string _directory = string.Empty;

    [SetUp]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"geodc-create-log-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public void Default_path_is_named_for_the_sample_project_under_the_state_directory()
    {
        SampleProjectCreateLog log = new(DriverSampleProject.Lifecycle);

        Assert.That(log.Path,
            Is.EqualTo(DriverStateDirectory.Resolve("sample-projects", "sample-lifecycle-create.log")));
    }

    [Test]
    public void Events_are_stamped_and_process_output_is_indented_beneath_them()
    {
        SampleProjectCreateLog log = new(DriverSampleProject.Default, Path.Combine(_directory, "sample-create.log"),
            new FixedTime(Now));

        log.Event("Running dotnet run");
        log.Output(["Seed: 42", "Project fingerprint: abc"]);

        Assert.That(log.Lines, Is.EqualTo(new[]
        {
            "2026-11-03T01:02:03.0000000Z sample create sample (preset sample)",
            "2026-11-03T01:02:03.0000000Z Running dotnet run",
            "    Seed: 42",
            "    Project fingerprint: abc"
        }));
    }

    [Test]
    public void Save_creates_the_directory_and_replaces_an_earlier_run()
    {
        string path = Path.Combine(_directory, "sample-projects", "sample-create.log");
        SampleProjectCreateLog first = new(DriverSampleProject.Default, path, new FixedTime(Now));
        first.Event("first run");
        first.Save();
        SampleProjectCreateLog second = new(DriverSampleProject.Default, path, new FixedTime(Now));
        second.Event("second run");

        second.Save();

        Assert.That(File.ReadAllLines(path), Is.EqualTo(second.Lines));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}