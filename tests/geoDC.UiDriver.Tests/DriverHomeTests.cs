using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     The driver home holds the UI driver's own files. The harness's shim names it, and without it the working
///     directory is the home, as it is for a driver run straight from geoDC.
/// </summary>
[TestFixture]
public sealed class DriverHomeTests
{
    private static readonly string WorkingDirectory = Path.GetFullPath("/work/geodc");

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Without_a_configured_home_the_working_directory_is_the_home(string? configured)
    {
        Assert.That(DriverHome.Locate(configured, WorkingDirectory), Is.EqualTo(WorkingDirectory));
    }

    [Test]
    public void A_rooted_home_is_used_as_it_is()
    {
        string harness = Path.GetFullPath("/work/harness");

        Assert.That(DriverHome.Locate(harness, WorkingDirectory), Is.EqualTo(harness));
    }

    [Test]
    public void A_relative_home_resolves_against_the_working_directory()
    {
        Assert.That(DriverHome.Locate("..", WorkingDirectory), Is.EqualTo(Path.GetFullPath("/work")));
    }
}