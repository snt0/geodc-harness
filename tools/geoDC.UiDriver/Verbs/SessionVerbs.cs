using System.IO;
using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     The verbs that manage the attached instance rather than read or drive it: <c>attach</c>, <c>kill</c>,
///     <c>status</c> and <c>rebuild</c>.
/// </summary>
internal static class SessionVerbs
{
    internal static void Attach(CliArgs cli)
    {
        DriverTarget target = DriverTarget.Parse(cli.Value("--target"));
        if (cli.Has("--passive") && !cli.Has("--launch"))
        {
            throw new DriverException("bad-arguments",
                "--passive applies only with --launch; an attached instance keeps the mode it was launched in.", 1);
        }

        using GeoDcSession session = GeoDcSession.Attach(target,
            cli.Has("--launch"), cli.Value("--project"), cli.Timeout(30), cli.Has("--passive"));
        JsonOutput.Success("attach", DescribeSession(session));
    }

    /// <summary>
    ///     Kills the driver's instance (entire tree) and removes its instance file, so a later <c>rebuild</c> without
    ///     <c>--target</c> has nothing to repeat. Idempotent: succeeds with wasRunning=false when no instance exists.
    /// </summary>
    internal static void Kill(CliArgs cli)
    {
        DriverTarget target = DriverTarget.Parse(cli.Value("--target"));
        int? killedPid = KillInstance(target);

        // An instance file whose process had already gone is removed too: kill ends the instance, not only the process.
        DriverInstance.Delete(target);
        if (killedPid is not { } pid)
        {
            JsonOutput.Success("kill",
                new { wasRunning = false, target = target.Name, instanceFile = DriverInstance.FilePath(target) });
            return;
        }

        JsonOutput.Success("kill",
            new { wasRunning = true, pid, target = target.Name, instanceFile = DriverInstance.FilePath(target) });
    }

    /// <summary>
    ///     Reports the driver's instance of the target without attaching: its process, whether it is still running,
    ///     how it was launched, and whether its build output on disk is newer than it (<c>stale</c>, with the newest
    ///     such file as <c>newerBuild</c>). A target with no instance file reports <c>hasInstance: false</c>, so this
    ///     verb does not fail when there is no instance.
    /// </summary>
    internal static void Status(CliArgs cli)
    {
        DriverTarget target = DriverTarget.Parse(cli.Value("--target"));
        DriverInstanceRecord? instance = DriverInstance.TryRead(target);
        bool alive;
        using (System.Diagnostics.Process? process = instance is null ? null : DriverInstance.TryOpen(target))
        {
            alive = process is not null;
        }

        BuildStaleness staleness = alive && instance is not null
            ? StaleBuildCheck.Evaluate(target, instance)
            : BuildStaleness.Fresh;
        JsonOutput.Success("status", new
        {
            target = target.Name,
            hasInstance = instance is not null,
            pid = instance?.Pid,
            alive,
            launchedUtc = instance?.StartTimeUtc,
            workingDirectory = instance?.WorkingDirectory,
            mode = instance is null ? null : instance.Passive ? "passive" : "active",
            executable = instance?.ExecutablePath,
            projectPath = instance?.ProjectPath,
            stale = staleness.Stale,
            newerBuild = staleness.Newest is { } newest ? new { path = newest.Path, writtenUtc = newest.WrittenUtc } : null,
            instanceFile = DriverInstance.FilePath(target)
        });
    }

    /// <summary>
    ///     Kills the driver's instance, builds its project, and launches the build with the instance's arguments, as
    ///     <c>kill</c>, <c>dotnet build</c> and <c>attach --launch</c> would in turn (see <see cref="RebuildPlan" /> for how
    ///     the arguments are chosen). A failed build fails with <c>build-failed</c>, its first errors as
    ///     <c>error.errors</c>, and leaves the app stopped. The instance file stays, so the next <c>rebuild</c> repeats
    ///     the same launch.
    ///     <para>
    ///         <c>--then &lt;script&gt; [--var name=value ...]</c> runs a script against the relaunched app, as
    ///         <c>do --file</c> would. Its step lines and summary follow the rebuild line, and its exit code is the
    ///         command's. The script is parsed before the app is stopped, so a script that cannot run fails the
    ///         command with the app untouched. A failed build skips the script, and a failing step leaves the app
    ///         running.
    ///     </para>
    /// </summary>
    internal static int Rebuild(string[] args, string? reportPath)
    {
        (string[] rebuildArgs, DoInvocation? then) = RebuildArguments.Split(args, DriverHome.ReadAllText);
        if (reportPath is not null)
        {
            DoReport.Active = then is not null
                ? new DoReport(reportPath, then, DateTime.UtcNow)
                : throw new DriverException("bad-arguments", "--report applies to rebuild only with --then <script>.", 1);
        }

        CliArgs cli = new(rebuildArgs);
        JsonOutput.WarnFrom(() => StaleBuildCheck.WarningFor(cli.Value("--target")));
        DriverTarget target = DriverTarget.Parse(cli.Value("--target"));
        RebuildPlan plan = RebuildPlan.Create(target, cli.Value("--target") is not null, DriverInstance.TryRead(target),
            cli.Value("--project"), cli.Has("--passive"), cli.Has("--active"),
            Environment.GetEnvironmentVariable("GEODC_APP_EXE"), GeoDcSession.WriteTimeUtc, DriverHome.Directory);

        // The instance file is kept through the build: if it fails, the next rebuild still knows how to launch the app.
        int? killedPid = KillInstance(target);
        DotnetBuildResult build = DotnetBuild.Run(plan);
        object buildSummary = new
        {
            project = plan.Target.ProjectFile,
            configuration = plan.Configuration,
            exitCode = build.ExitCode,
            durationMs = build.DurationMs,
            log = build.LogPath
        };
        if (!build.Succeeded)
        {
            throw new DriverException("build-failed",
                $"dotnet build {plan.Target.ProjectFile} -c {plan.Configuration} failed with exit code {build.ExitCode} " +
                $"and {build.Errors.Count} error(s); the app was not relaunched. Fix the errors and run rebuild again.",
                1, details: new
                {
                    errors = build.Errors.First,
                    errorCount = build.Errors.Count,
                    killedPid,
                    build = buildSummary
                });
        }

        if (!File.Exists(plan.ExecutablePath))
        {
            throw new DriverException("app-not-found",
                $"dotnet build {plan.Target.ProjectFile} -c {plan.Configuration} succeeded but did not produce " +
                $"{plan.ExecutablePath}.", 3, details: new { killedPid, build = buildSummary });
        }

        using GeoDcSession session = GeoDcSession.Launch(target, plan.ExecutablePath, plan.ProjectPath, cli.Timeout(30),
            plan.Passive);
        JsonOutput.Success("rebuild", new
        {
            target = target.Name,
            killedPid,
            reusedInstance = plan.ReusesInstance,
            build = buildSummary,
            launch = DescribeSession(session)
        });
        return then is null ? 0 : DoVerb.RunSteps(session, then);
    }

    /// <summary>
    ///     Kills the whole process tree of the driver's instance and waits for it to exit, leaving the instance file in
    ///     place. Returns the process id it killed, or null when the driver's instance was not running.
    /// </summary>
    private static int? KillInstance(DriverTarget target)
    {
        using System.Diagnostics.Process? process = GeoDcSession.FindInstance(target);
        if (process is null)
        {
            return null;
        }

        int pid = process.Id;
        process.Kill(true);
        if (!process.WaitForExit(10_000))
        {
            throw new DriverException("kill-timeout", $"Process {pid} did not exit within 10s of Kill.", 1);
        }

        return pid;
    }

    /// <summary>The attached instance as <c>attach</c> reports it; <c>rebuild</c> reports its relaunch the same way.</summary>
    private static object DescribeSession(GeoDcSession session)
    {
        AutomationElement mainWindow = session.MainWindow();
        string? executable = session.Process.MainModule?.FileName;
        return new
        {
            pid = session.Process.Id,
            mainWindowHandle = $"0x{mainWindow.Properties.NativeWindowHandle.ValueOrDefault:X8}",
            mainWindowTitle = mainWindow.Properties.Name.ValueOrDefault ?? string.Empty,
            launched = session.Launched,
            mode = session.Passive ? "passive" : "active",
            executable,
            executableBuiltUtc = executable is null ? (DateTime?)null : File.GetLastWriteTimeUtc(executable),
            projectPath = session.ProjectPath,
            instanceFile = DriverInstance.FilePath(session.Target)
        };
    }
}