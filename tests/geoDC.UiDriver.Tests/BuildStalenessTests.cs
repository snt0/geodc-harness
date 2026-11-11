using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     The driver's instance is stale when its build output on disk was written after it started, including a compiled
///     assembly that could not be copied over the running instance's locked output.
/// </summary>
[TestFixture]
public sealed class BuildStalenessTests
{
    private static readonly DateTime Started = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private string _root = string.Empty;
    private string _output = string.Empty;
    private string _executable = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "geodc-ui-driver-stale-" + Guid.NewGuid().ToString("N"));
        _output = Path.Combine(_root, "src", "geoDC.App", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(_output);
        _executable = Path.Combine(_output, "geoDC.App.exe");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public void Nothing_written_after_the_start_is_fresh()
    {
        BuildStaleness staleness = BuildStaleness.Evaluate(Started,
            [new BuildArtifact("a.dll", Started.AddMinutes(-5)), new BuildArtifact("b.dll", Started)]);

        Assert.Multiple(() =>
        {
            Assert.That(staleness.Stale, Is.False);
            Assert.That(staleness.Newest, Is.Null);
        });
    }

    [Test]
    public void The_newest_output_written_after_the_start_is_reported()
    {
        BuildArtifact newest = new("b.dll", Started.AddMinutes(3));

        BuildStaleness staleness = BuildStaleness.Evaluate(Started,
            [new BuildArtifact("a.dll", Started.AddMinutes(1)), newest, new BuildArtifact("c.dll", Started)]);

        Assert.Multiple(() =>
        {
            Assert.That(staleness.Stale, Is.True);
            Assert.That(staleness.Newest, Is.EqualTo(newest));
        });
    }

    [Test]
    public void Collect_reads_the_assemblies_and_executables_beside_the_executable_only()
    {
        string exe = Write(_executable, Started);
        string dll = Write(Path.Combine(_output, "geoDC.App.UI.dll"), Started);
        Write(Path.Combine(_output, "geoDC.App.pdb"), Started.AddHours(1));
        Write(Path.Combine(_output, "appsettings.json"), Started.AddHours(1));
        Write(Path.Combine(_output, "de", "geoDC.App.resources.dll"), Started.AddHours(1));

        IReadOnlyList<BuildArtifact> artifacts = Collect("Debug");

        Assert.That(artifacts.Select(artifact => artifact.Path), Is.EquivalentTo(new[] { exe, dll }));
    }

    [Test]
    public void A_compiled_assembly_the_locked_output_did_not_receive_makes_the_instance_stale()
    {
        Write(_executable, Started.AddMinutes(-10));
        Write(Path.Combine(_output, "geoDC.App.UI.dll"), Started.AddMinutes(-10));
        string compiled = Write(
            Path.Combine(_root, "src", "geoDC.App.UI", "obj", "Debug", "net10.0-windows", "geoDC.App.UI.dll"),
            Started.AddMinutes(2));
        Write(Path.Combine(_root, "src", "geoDC.App.UI", "obj", "Release", "net10.0-windows", "geoDC.App.UI.dll"),
            Started.AddMinutes(4));

        BuildStaleness staleness = BuildStaleness.Evaluate(Started, Collect("Debug"));

        Assert.That(staleness.Newest?.Path, Is.EqualTo(compiled));
    }

    [Test]
    public void Without_a_known_configuration_compiled_assemblies_are_not_read()
    {
        Write(Path.Combine(_output, "geoDC.App.UI.dll"), Started.AddMinutes(-10));
        Write(Path.Combine(_root, "src", "geoDC.App.UI", "obj", "Debug", "net10.0-windows", "geoDC.App.UI.dll"),
            Started.AddMinutes(2));

        Assert.That(BuildStaleness.Evaluate(Started, Collect(null)).Stale, Is.False);
    }

    [Test]
    public void Another_configurations_newer_build_makes_the_instance_stale()
    {
        string exe = Write(_executable, Started.AddMinutes(-10));
        string release = Write(Path.Combine(_root, "src", "geoDC.App", "bin", "Release", "net10.0-windows",
            "geoDC.App.exe"), Started.AddMinutes(1));
        string missing = Path.Combine(_root, "missing.exe");

        IReadOnlyList<BuildArtifact> artifacts = Collect("Debug", _executable, release, missing);

        Assert.Multiple(() =>
        {
            Assert.That(artifacts.Count(artifact => artifact.Path == exe), Is.EqualTo(1));
            Assert.That(artifacts.Any(artifact => artifact.Path == missing), Is.False);
            Assert.That(BuildStaleness.Evaluate(Started, artifacts).Newest?.Path, Is.EqualTo(release));
        });
    }

    [Test]
    public void A_missing_output_directory_collects_nothing_from_it()
    {
        Directory.Delete(_output, true);

        Assert.That(Collect("Debug"), Is.Empty);
    }

    private IReadOnlyList<BuildArtifact> Collect(string? configuration, params string[] otherExecutables)
    {
        return BuildStaleness.Collect(_executable, configuration, Path.Combine(_root, "src"), otherExecutables);
    }

    private static string Write(string path, DateTime writtenUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return Path.GetFullPath(path);
    }
}