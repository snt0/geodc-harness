using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class DriverTargetTests
{
    private static readonly DateTime Built = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Each_configuration_builds_its_own_executable_from_the_project_file()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DriverTarget.App.ProjectFile, Is.EqualTo("src/geoDC.App/geoDC.App.csproj"));
            Assert.That(DriverTarget.App.ExeCandidates, Is.EqualTo(new[]
            {
                "src/geoDC.App/bin/Debug/net10.0-windows10.0.17763.0/geoDC.App.exe",
                "src/geoDC.App/bin/Release/net10.0-windows10.0.17763.0/geoDC.App.exe"
            }));
            Assert.That(DriverTarget.Sample.ExecutablePath("Release"),
                Is.EqualTo("src/geoDC.App.UI.Sample/bin/Release/net10.0-windows/geoDC.App.UI.Sample.exe"));
        });
    }

    [Test]
    public void The_newest_build_wins_and_a_tie_goes_to_the_first_configuration()
    {
        string debug = DriverTarget.App.ExecutablePath("Debug");
        string release = DriverTarget.App.ExecutablePath("Release");

        Assert.Multiple(() =>
        {
            Assert.That(DriverTarget.App.NewestConfiguration(_ => null), Is.Null);
            Assert.That(DriverTarget.App.NewestConfiguration(path => path == release ? Built : Built.AddHours(-1)),
                Is.EqualTo("Release"));
            Assert.That(DriverTarget.App.NewestConfiguration(path => path == debug ? Built : null),
                Is.EqualTo("Debug"));
            Assert.That(DriverTarget.App.NewestConfiguration(_ => Built), Is.EqualTo("Debug"));
        });
    }

    [Test]
    public void An_executable_is_matched_to_its_configuration_by_full_path()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DriverTarget.App.ConfigurationOf(Path.GetFullPath(DriverTarget.App.ExecutablePath("Release"))),
                Is.EqualTo("Release"));
            Assert.That(DriverTarget.App.ConfigurationOf(DriverTarget.Sample.ExecutablePath("Debug")), Is.Null);
        });
    }
}