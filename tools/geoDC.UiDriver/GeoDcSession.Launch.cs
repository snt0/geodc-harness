using System.Diagnostics;
using System.IO;
using FlaUI.UIA3;

namespace geoDC.UiDriver;

// Locating the target build, finding the driver's instance, and launching a new one.
public sealed partial class GeoDcSession
{
    /// <summary>
    ///     Directory of the target's build output, or null when no build exists. Verbs that must work
    ///     without a running instance (kill, logs) use it.
    /// </summary>
    public static string? TryResolveExeDirectory(DriverTarget target)
    {
        try
        {
            return Path.GetDirectoryName(ResolveExe(target));
        }
        catch (DriverException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The driver's instance of <paramref name="target" /> if it is still the process the UI driver launched, else
    ///     null.
    ///     The caller disposes it. Unlike <see cref="Attach" />, it creates no automation instance and never launches.
    /// </summary>
    public static Process? FindInstance(DriverTarget target)
    {
        return DriverInstance.TryOpen(target);
    }

    /// <summary>
    ///     Launches <paramref name="exePath" /> as the driver's instance of the target and waits up to
    ///     <paramref name="timeout" /> for its main window. For a target that opens a project package, it opens
    ///     <paramref name="projectOverride" /> (relative to the <see cref="DriverHome" />), or the default sample
    ///     project when that is null. It first clears a lock left behind by a previous holder that has gone, and the
    ///     app runs against a fresh copy of the package's Shore Control Register (<see cref="DriverRegister" />).
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>sample-project-missing</c>, <c>project-locked</c>, <c>register-busy</c>, <c>launch-failed</c>,
    ///     <c>instance-write-failed</c> or <c>launch-timeout</c>, all with exit code 3; <c>bad-arguments</c> for a
    ///     package passed to a target that opens none.
    /// </exception>
    public static GeoDcSession Launch(DriverTarget target, string exePath, string? projectOverride, TimeSpan timeout,
        bool passive)
    {
        string? projectPath = null;

        // ShellExecute so the launched app does not inherit our stdout handle. Under UseShellExecute=false it
        // does, and a caller that pipes UI driver output (`geodc-ui attach --launch | tail`) then blocks until the app
        // exits: the reader waits for EOF, and the app holds the write end open for the whole session.
        ProcessStartInfo startInfo = new()
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true
        };

        if (target.OpensProject)
        {
            projectPath = DriverHome.Resolve(projectOverride ?? DriverTarget.DefaultSampleProjectPath);
            if (!File.Exists(projectPath))
            {
                throw new DriverException("sample-project-missing",
                    $"Project package not found: {projectPath}. Run `geodc-ui sample create` to create the sample " +
                    "project (add --lifecycle for sample-lifecycle.gdcproj).",
                    3);
            }

            // A killed or crashed instance leaves its lock behind, and geoDC would open behind a modal lock prompt
            // that blocks every UI Automation call. A lock whose process is gone is cleared. Any other lock stops
            // the launch.
            ProjectLock projectLock = ProjectLockFile.ClearIfStale(projectPath);
            if (projectLock.BlocksLaunch)
            {
                throw new DriverException("project-locked",
                    $"{projectPath} is locked by {projectLock.Describe()} ({ProjectLockFile.PathFor(projectPath)}). " +
                    "Close that geoDC session, or run `geodc-ui kill`, then launch again.", 3);
            }

            startInfo.ArgumentList.Add("--open");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add(DriverRegister.Prepare(projectPath,
                DriverStateDirectory.Resolve("registers", $"{target.Name}.sqlite")));
        }
        else if (projectOverride is not null)
        {
            throw new DriverException("bad-arguments",
                $"Target '{target.Name}' does not open project packages; drop --project.", 1);
        }

        // ShellExecute cannot take a per-child environment, but the child inherits this process's environment, so
        // the request is set on this short-lived UI driver process instead. Every UI driver launch starts behind
        // the developer's windows. The flag only chooses whether the app may come forward afterwards (active) or
        // never (passive).
        Environment.SetEnvironmentVariable(BackgroundLaunchVariable, passive ? "passive" : "active");
        Process process = Process.Start(startInfo) ??
                          throw new DriverException("launch-failed", $"Failed to start {exePath}.", 3);
        try
        {
            DriverInstance.Write(target, process, exePath, passive, projectPath);
        }
        catch (Exception ex)
        {
            process.Kill(true);
            process.Dispose();
            throw new DriverException("instance-write-failed",
                $"Started geoDC but could not record UI driver ownership at {DriverInstance.FilePath(target)}: " +
                ex.Message,
                3);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (process.HasExited)
            {
                // The instance file stays, so a `rebuild` after fixing the crash launches it again with the same
                // arguments.
                throw new DriverException("launch-failed",
                    $"geoDC exited during startup with code {process.ExitCode}. Check logs/ for details.", 3);
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                // Shown without activation, the window still lands on top of the z-order, so it is moved under
                // whatever the developer is working in. PrintWindow can still capture it there.
                WindowActivation.SendToBack(process.MainWindowHandle);

                return new GeoDcSession(target, new UIA3Automation(), process, true, projectPath, passive);
            }

            Thread.Sleep(250);
        }

        throw new DriverException("launch-timeout",
            $"geoDC did not show a main window within {timeout.TotalSeconds:0.#}s.", 3);
    }

    /// <summary>The last write time of <paramref name="path" />, or null when the file does not exist.</summary>
    public static DateTime? WriteTimeUtc(string path)
    {
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    private static string ResolveExe(DriverTarget target)
    {
        if (target.OpensProject &&
            Environment.GetEnvironmentVariable("GEODC_APP_EXE") is { Length: > 0 } overridePath)
        {
            return File.Exists(overridePath)
                ? Path.GetFullPath(overridePath)
                : throw new DriverException("app-not-found", $"GEODC_APP_EXE points at a missing file: {overridePath}",
                    3);
        }

        // Newest build wins, not the first configuration listed: a developer who builds Release must not be
        // silently handed a stale Debug binary (or the reverse). `attach` reports which one it launched.
        if (target.NewestConfiguration(WriteTimeUtc) is { } configuration)
        {
            return Path.GetFullPath(target.ExecutablePath(configuration));
        }

        throw new DriverException("app-not-found",
            $"Build output for target '{target.Name}' not found. Build it first: dotnet build {target.ProjectDirectory}" +
            (target.OpensProject ? " — or set GEODC_APP_EXE." : "."), 3);
    }
}