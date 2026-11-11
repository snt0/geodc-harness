using System.Text.Json;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     <c>sample create</c> turns a test-data generator run into a sample project the UI driver can use: the package,
///     its register and the provenance file.
/// </summary>
[TestFixture]
public sealed class DriverSampleProjectTests
{
    private static readonly string[] GeneratorOutput =
    [
        "Preset: sample",
        "Seed: 20251003",
        "Built scenario: 1 job(s), 12 feature(s), 3 instrument(s), 6 series, 1 calibration(s), 0 verification(s), " +
        "1 GNSS check(s).",
        "Domain fingerprint: domain-hash",
        "Wrote: /tmp/staging/TestProject.gdcproj",
        "Raw:   not written",
        "Register: /tmp/staging/shore-control.sqlite",
        "Register fingerprint: register-hash",
        "Project fingerprint: project-hash"
    ];

    private string _root = string.Empty;
    private string _staging = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "geodc-ui-driver-sample-project-" + Guid.NewGuid().ToString("N"));
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_staging);
        File.WriteAllText(Path.Combine(_staging, "TestProject.gdcproj"), "package");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public void Each_sample_project_has_its_package_register_and_provenance_file_in_dev_sample_projects()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DriverSampleProject.Default.PackagePath, Is.EqualTo("dev/sample-projects/sample.gdcproj"));
            Assert.That(DriverSampleProject.Default.RegisterPath,
                Is.EqualTo("dev/sample-projects/sample-register.sqlite"));
            Assert.That(DriverSampleProject.Default.ProvenancePath,
                Is.EqualTo("dev/sample-projects/sample.fingerprint.json"));
            Assert.That(DriverSampleProject.Lifecycle.PackagePath,
                Is.EqualTo("dev/sample-projects/sample-lifecycle.gdcproj"));
            Assert.That(DriverSampleProject.Default.PackagePath, Is.EqualTo(DriverTarget.DefaultSampleProjectPath));
        }
    }

    [Test]
    public void The_generator_runs_the_sample_project_preset_into_the_staging_folder()
    {
        Assert.That(DriverSampleProject.Lifecycle.GeneratorArguments("/tmp/staging"),
            Is.EqualTo(new[] { "/tmp/staging", "--preset=lifecycle-coverage" }));
    }

    [Test]
    public void Provenance_comes_from_the_generator_output()
    {
        DateTime generated = new(2026, 11, 2, 21, 0, 0, DateTimeKind.Utc);

        SampleProjectProvenance provenance = DriverSampleProject.Default.ReadProvenance(GeneratorOutput, generated);

        Assert.That(provenance, Is.EqualTo(new SampleProjectProvenance(20251003, "sample", "minimal", "domain-hash",
            "project-hash", "register-hash", generated)));
    }

    [Test]
    public void The_register_line_is_not_its_fingerprint_line()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DriverSampleProject.GeneratedRegisterPath(GeneratorOutput),
                Is.EqualTo("/tmp/staging/shore-control.sqlite"));
            Assert.That(DriverSampleProject.GeneratedRegisterPath(["Register fingerprint: register-hash"]), Is.Null);
        }
    }

    [Test]
    public void A_run_without_a_register_records_none()
    {
        string[] output = [.. GeneratorOutput.Where(line => !line.StartsWith("Register", StringComparison.Ordinal))];

        SampleProjectProvenance provenance = DriverSampleProject.Default.ReadProvenance(output, DateTime.UtcNow);

        Assert.That(provenance.RegisterFingerprint, Is.Null);
    }

    [Test]
    public void Output_without_a_project_fingerprint_is_refused()
    {
        string[] output = [.. GeneratorOutput.Where(line => !line.StartsWith("Project", StringComparison.Ordinal))];

        DriverException exception = Assert.Throws<DriverException>(() =>
            DriverSampleProject.Default.ReadProvenance(output, DateTime.UtcNow))!;

        Assert.That(exception.Code, Is.EqualTo("sample-project-generator-output"));
    }

    [Test]
    public void Install_copies_the_package_and_register_and_writes_the_provenance_file()
    {
        string register = Path.Combine(_staging, "shore-control.sqlite");
        File.WriteAllText(register, "register");
        SampleProjectProvenance provenance =
            DriverSampleProject.Default.ReadProvenance(GeneratorOutput, DateTime.UtcNow);

        DriverSampleProject.Default.Install(_root, _staging, register, provenance);

        SampleProjectState state = DriverSampleProject.Default.ReadState(_root);
        using JsonDocument provenanceFile =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, DriverSampleProject.Default.ProvenancePath)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(_root, DriverSampleProject.Default.PackagePath)),
                Is.EqualTo("package"));
            Assert.That(File.ReadAllText(Path.Combine(_root, DriverSampleProject.Default.RegisterPath)),
                Is.EqualTo("register"));
            Assert.That(state.Ready, Is.True);
            Assert.That(state.Provenance, Is.EqualTo(provenance));
            Assert.That(provenanceFile.RootElement.GetProperty("projectFingerprint").GetString(),
                Is.EqualTo("project-hash"));
            Assert.That(provenanceFile.RootElement.GetProperty("seed").GetInt32(), Is.EqualTo(20251003));
            Assert.That(provenanceFile.RootElement.GetProperty("scenario").GetString(), Is.EqualTo("minimal"));
        }
    }

    [Test]
    public void Install_without_a_register_removes_the_last_one()
    {
        Directory.CreateDirectory(Path.Combine(_root, DriverSampleProject.SampleProjectDirectory));
        File.WriteAllText(Path.Combine(_root, DriverSampleProject.Default.RegisterPath), "old register");
        SampleProjectProvenance provenance =
            DriverSampleProject.Default.ReadProvenance(GeneratorOutput, DateTime.UtcNow);

        DriverSampleProject.Default.Install(_root, _staging, null, provenance);

        Assert.That(DriverSampleProject.Default.ReadState(_root).RegisterExists, Is.False);
    }

    [Test]
    public void A_missing_or_unreadable_provenance_file_leaves_the_sample_project_not_ready()
    {
        Directory.CreateDirectory(Path.Combine(_root, DriverSampleProject.SampleProjectDirectory));
        File.WriteAllText(Path.Combine(_root, DriverSampleProject.Default.PackagePath), "package");

        SampleProjectState missing = DriverSampleProject.Default.ReadState(_root);
        File.WriteAllText(Path.Combine(_root, DriverSampleProject.Default.ProvenancePath), "{ not json");
        SampleProjectState unreadable = DriverSampleProject.Default.ReadState(_root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.PackageExists, Is.True);
            Assert.That(missing.Ready, Is.False);
            Assert.That(unreadable.Provenance, Is.Null);
            Assert.That(DriverSampleProject.Lifecycle.ReadState(_root).PackageExists, Is.False);
        }
    }
}