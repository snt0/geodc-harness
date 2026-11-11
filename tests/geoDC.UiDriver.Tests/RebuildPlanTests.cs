using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     <c>rebuild</c> repeats the launch of the driver's instance: the arguments the command line leaves out come from
///     the instance, and the configuration it builds is the one it launches.
/// </summary>
[TestFixture]
public sealed class RebuildPlanTests
{
    private static readonly DateTime Built = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Func<string, DateTime?> NothingBuilt = _ => null;

    // The driver home, away from the working directory, so a relative --project is seen to resolve against it.
    private static readonly string Home = Path.GetFullPath("/harness");

    private static DriverInstanceRecord AppInstance(string configuration = "Release", bool passive = true,
        string? projectPath = "/work/other.gdcproj")
    {
        return new DriverInstanceRecord("app", 4242, Built,
            Path.GetFullPath(DriverTarget.App.ExecutablePath(configuration)), passive, projectPath, "/work");
    }

    private static RebuildPlan Create(DriverTarget target, DriverInstanceRecord? instance, bool targetGiven = false,
        string? project = null, bool passive = false, bool active = false, string? executableOverride = null,
        Func<string, DateTime?>? writeTimeUtc = null)
    {
        return RebuildPlan.Create(target, targetGiven, instance, project, passive, active, executableOverride,
            writeTimeUtc ?? NothingBuilt, Home);
    }

    [Test]
    public void Without_an_instance_or_a_target_there_is_nothing_to_repeat()
    {
        DriverException? failure = Assert.Throws<DriverException>(() => Create(DriverTarget.App, null));

        Assert.Multiple(() =>
        {
            Assert.That(failure?.Code, Is.EqualTo("no-instance"));
            Assert.That(failure?.ExitCode, Is.EqualTo(1));
        });
    }

    [Test]
    public void An_instance_supplies_every_launch_argument()
    {
        RebuildPlan plan = Create(DriverTarget.App, AppInstance());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Configuration, Is.EqualTo("Release"));
            Assert.That(plan.ExecutablePath, Is.EqualTo(Path.GetFullPath(DriverTarget.App.ExecutablePath("Release"))));
            Assert.That(plan.ProjectPath, Is.EqualTo(Path.GetFullPath("/work/other.gdcproj")));
            Assert.That(plan.Passive, Is.True);
            Assert.That(plan.ReusesInstance, Is.True);
        });
    }

    [Test]
    public void The_command_line_overrides_the_instance()
    {
        RebuildPlan plan = Create(DriverTarget.App, AppInstance(), project: "dev/sample-projects/sample.gdcproj",
            active: true);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ProjectPath, Is.EqualTo(Path.GetFullPath(DriverTarget.DefaultSampleProjectPath, Home)));
            Assert.That(plan.Passive, Is.False);
        });
    }

    [Test]
    public void A_target_without_an_instance_launches_the_sample_project_actively_from_the_newest_build()
    {
        string release = DriverTarget.App.ExecutablePath("Release");
        Func<string, DateTime?> writeTime = path => path == release
            ? Built
            : path == DriverTarget.App.ExecutablePath("Debug")
                ? Built.AddHours(-1)
                : null;

        RebuildPlan plan = Create(DriverTarget.App, null, true, writeTimeUtc: writeTime);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Configuration, Is.EqualTo("Release"));
            Assert.That(plan.ProjectPath, Is.EqualTo(Path.GetFullPath(DriverTarget.DefaultSampleProjectPath, Home)));
            Assert.That(plan.Passive, Is.False);
            Assert.That(plan.ReusesInstance, Is.False);
        });
    }

    [Test]
    public void With_nothing_built_the_default_configuration_is_built()
    {
        RebuildPlan plan = Create(DriverTarget.Sample, null, true, passive: true);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Configuration, Is.EqualTo(RebuildPlan.DefaultConfiguration));
            Assert.That(plan.ProjectPath, Is.Null);
            Assert.That(plan.Passive, Is.True);
        });
    }

    [Test]
    public void An_instance_of_an_executable_outside_the_targets_builds_takes_the_newest_build()
    {
        DriverInstanceRecord instance =
            AppInstance() with { ExecutablePath = Path.GetFullPath("/elsewhere/geoDC.App.exe") };
        string debug = DriverTarget.App.ExecutablePath("Debug");

        RebuildPlan plan = Create(DriverTarget.App, instance, writeTimeUtc: path => path == debug ? Built : null);

        Assert.That(plan.Configuration, Is.EqualTo("Debug"));
    }

    [Test]
    public void The_build_arguments_build_the_configuration_that_is_launched()
    {
        RebuildPlan plan = Create(DriverTarget.App, AppInstance());

        IReadOnlyList<string> arguments = plan.BuildArguments("/work/.ui-driver/builds/app.log");

        Assert.Multiple(() =>
        {
            Assert.That(arguments[0], Is.EqualTo("build"));
            Assert.That(arguments[1], Is.EqualTo(Path.GetFullPath("src/geoDC.App/geoDC.App.csproj")));
            Assert.That(arguments[arguments.ToList().IndexOf("-c") + 1], Is.EqualTo(plan.Configuration));
            Assert.That(plan.ExecutablePath,
                Does.Contain(Path.Combine("bin", plan.Configuration, DriverTarget.App.TargetFramework)));
            Assert.That(arguments, Has.Some.EqualTo("-fileLoggerParameters:LogFile=/work/.ui-driver/builds/app.log;Verbosity=minimal"));
            Assert.That(arguments, Has.Some.EqualTo("-noConsoleLogger"));
        });
    }

    [Test]
    public void Passive_and_active_together_are_rejected()
    {
        DriverException? failure = Assert.Throws<DriverException>(() =>
            Create(DriverTarget.App, AppInstance(), passive: true, active: true));

        Assert.That(failure?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void The_sample_opens_no_package()
    {
        DriverException? failure = Assert.Throws<DriverException>(() =>
            Create(DriverTarget.Sample, null, true, "dev/sample-projects/sample.gdcproj"));

        Assert.That(failure?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void An_overridden_app_executable_cannot_be_rebuilt_but_the_sample_ignores_the_override()
    {
        DriverException? failure = Assert.Throws<DriverException>(() =>
            Create(DriverTarget.App, AppInstance(), executableOverride: @"C:\builds\geoDC.App.exe"));

        Assert.Multiple(() =>
        {
            Assert.That(failure?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(Create(DriverTarget.Sample, null, true, executableOverride: @"C:\builds\geoDC.App.exe").Target,
                Is.EqualTo(DriverTarget.Sample));
        });
    }
}