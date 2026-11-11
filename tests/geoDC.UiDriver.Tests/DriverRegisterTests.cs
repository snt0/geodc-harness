using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     A UI driver launch gives geoDC a throwaway copy of the sample project's Shore Control Register, not the
///     developer's own.
/// </summary>
[TestFixture]
public sealed class DriverRegisterTests
{
    private string _directory = string.Empty;
    private string _project = string.Empty;
    private string _working = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "geodc-ui-driver-register-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _project = Path.Combine(_directory, "sample.gdcproj");
        _working = Path.Combine(_directory, ".ui-driver", "registers", "app.sqlite");
        File.WriteAllText(_project, "package");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void The_sample_project_register_sits_beside_the_package_with_its_name()
    {
        Assert.That(DriverRegister.SampleProjectRegisterPathFor(_project),
            Is.EqualTo(Path.Combine(_directory, "sample-register.sqlite")));
    }

    [Test]
    public void A_launch_works_on_a_fresh_copy_of_the_sample_project_register()
    {
        File.WriteAllText(DriverRegister.SampleProjectRegisterPathFor(_project), "sample register");
        DriverRegister.Prepare(_project, _working);
        File.WriteAllText(_working, "changed by the last session");
        File.WriteAllText(_working + "-wal", "left over");

        string argument = DriverRegister.Prepare(_project, _working);

        Assert.Multiple(() =>
        {
            Assert.That(argument, Is.EqualTo($"--ConnectionStrings:ShoreControlStore=Data Source={_working}"));
            Assert.That(File.ReadAllText(_working), Is.EqualTo("sample register"));
            Assert.That(File.Exists(_working + "-wal"), Is.False);
            Assert.That(File.ReadAllText(DriverRegister.SampleProjectRegisterPathFor(_project)),
                Is.EqualTo("sample register"));
        });
    }

    [Test]
    public void A_package_without_a_register_gets_an_empty_one()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_working)!);
        File.WriteAllText(_working, "from an earlier launch");

        DriverRegister.Prepare(_project, _working);

        Assert.That(File.Exists(_working), Is.False, "geoDC creates an empty register where none exists");
    }
}