using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     What <c>rebuild</c> builds and then launches. The build configuration and the executable come from one choice,
///     so the instance launched is always the one just built.
/// </summary>
/// <param name="Target">The target to rebuild.</param>
/// <param name="Configuration">The configuration passed to <c>dotnet build -c</c>.</param>
/// <param name="ExecutablePath">The full path of the executable that configuration builds, which is then launched.</param>
/// <param name="ProjectPath">The full path of the project package to open, or null for a target that opens none.</param>
/// <param name="Passive">True to launch in passive mode.</param>
/// <param name="ReusesInstance">True when the launch arguments came from the driver's existing instance.</param>
public sealed record RebuildPlan(
    DriverTarget Target,
    string Configuration,
    string ExecutablePath,
    string? ProjectPath,
    bool Passive,
    bool ReusesInstance)
{
    /// <summary>The configuration built when neither the driver's instance nor an existing build names one.</summary>
    public const string DefaultConfiguration = "Debug";

    /// <summary>
    ///     Decides the rebuild from the command line and the driver's instance of the target. Each launch argument the
    ///     command line does not give is taken from the instance, alive or not. Otherwise it defaults to the default
    ///     sample project, active mode, and the configuration of the newest existing build, else
    ///     <see cref="DefaultConfiguration" />. The configuration is the instance's when its executable is one of the
    ///     target's builds.
    /// </summary>
    /// <param name="target">The target to rebuild.</param>
    /// <param name="targetGiven">True when <c>--target</c> was passed.</param>
    /// <param name="instance">The driver's instance of the target, or null when it has none.</param>
    /// <param name="projectOption">The <c>--project</c> value, or null.</param>
    /// <param name="passive">True when <c>--passive</c> was passed.</param>
    /// <param name="active">True when <c>--active</c> was passed.</param>
    /// <param name="executableOverride">The <c>GEODC_APP_EXE</c> value, or null.</param>
    /// <param name="writeTimeUtc">The last write time of a file, or null when it does not exist.</param>
    /// <param name="driverHome">The <see cref="DriverHome" />, which a relative <c>--project</c> resolves against.</param>
    /// <exception cref="DriverException">
    ///     <c>no-instance</c> when there is no instance to repeat and no <c>--target</c>; <c>bad-arguments</c> for
    ///     <c>--passive</c> with <c>--active</c>, <c>--project</c> for a target that opens no package, or an app
    ///     executable overridden by <c>GEODC_APP_EXE</c>, which <c>rebuild</c> cannot build.
    /// </exception>
    public static RebuildPlan Create(DriverTarget target, bool targetGiven, DriverInstanceRecord? instance,
        string? projectOption, bool passive, bool active, string? executableOverride,
        Func<string, DateTime?> writeTimeUtc, string driverHome)
    {
        if (instance is null && !targetGiven)
        {
            throw new DriverException("no-instance",
                $"No UI driver instance for target '{target.Name}' in {driverHome}, so there is no launch to " +
                "repeat. Pass --target app|sample to build and launch one, or run from the checkout whose " +
                "`attach --launch` created the instance.", 1);
        }

        if (passive && active)
        {
            throw new DriverException("bad-arguments", "--passive and --active cannot be combined.", 1);
        }

        if (!target.OpensProject && projectOption is not null)
        {
            throw new DriverException("bad-arguments",
                $"Target '{target.Name}' does not open project packages; drop --project.", 1);
        }

        if (target.OpensProject && !string.IsNullOrEmpty(executableOverride))
        {
            throw new DriverException("bad-arguments",
                $"GEODC_APP_EXE names the executable to launch ({executableOverride}), which rebuild cannot build. " +
                "Unset it, or build that executable yourself and use kill and attach --launch.", 1);
        }

        string configuration =
            (instance is null ? null : target.ConfigurationOf(instance.ExecutablePath)) ??
            target.NewestConfiguration(writeTimeUtc) ??
            DefaultConfiguration;
        string? projectPath = target.OpensProject
            ? Path.GetFullPath(projectOption ?? instance?.ProjectPath ?? DriverTarget.DefaultSampleProjectPath,
                Path.GetFullPath(driverHome))
            : null;
        bool launchPassive = passive || (!active && instance is { Passive: true });

        return new RebuildPlan(target, configuration, Path.GetFullPath(target.ExecutablePath(configuration)),
            projectPath, launchPassive, instance is not null);
    }

    /// <summary>
    ///     The <c>dotnet</c> arguments that build <see cref="Configuration" />, writing the MSBuild log to
    ///     <paramref name="logPath" /> and nothing to the console. Node reuse is off so no build node outlives the
    ///     build holding its output streams open.
    /// </summary>
    public IReadOnlyList<string> BuildArguments(string logPath)
    {
        return
        [
            "build", Path.GetFullPath(Target.ProjectFile), "-c", Configuration, "--nologo", "-noConsoleLogger",
            "-nodeReuse:false", $"-fileLoggerParameters:LogFile={logPath};Verbosity=minimal"
        ];
    }
}