using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace geoDC.UiDriver;

/// <summary>The outcome of a <c>dotnet build</c> run by <c>rebuild</c>.</summary>
/// <param name="ExitCode">The build's exit code; 0 is success.</param>
/// <param name="DurationMs">How long the build took.</param>
/// <param name="LogPath">The MSBuild log file the build wrote.</param>
/// <param name="Errors">The errors in the log and the console output.</param>
public sealed record DotnetBuildResult(int ExitCode, long DurationMs, string LogPath, BuildErrors Errors)
{
    /// <summary>
    ///     True when <see cref="ExitCode" /> is 0. Success is decided by the exit code, not by whether any errors were
    ///     parsed.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
///     Runs <c>dotnet build</c> for a <see cref="RebuildPlan" />. Nothing it prints reaches the UI driver's stdout,
///     which stays the one-line JSON contract. MSBuild writes its log to a file under <c>.ui-driver/builds/</c>, and
///     the little the CLI itself prints is read for errors and then dropped.
/// </summary>
public static class DotnetBuild
{
    /// <summary>How long a build may run before it is stopped.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    // Once the build has exited, how long to wait for the last of its console output.
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Builds the plan's project and configuration from the working directory.</summary>
    /// <exception cref="DriverException">
    ///     <c>build-not-started</c> when <c>dotnet</c> cannot be started; <c>build-timeout</c> when the build runs past
    ///     <see cref="Timeout" />, after it has been stopped.
    /// </exception>
    public static DotnetBuildResult Run(RebuildPlan plan)
    {
        string logPath = DriverStateDirectory.Resolve("builds", $"{plan.Target.Name}.log");
        Directory.CreateDirectory(DriverStateDirectory.Resolve("builds"));
        // A log left by an earlier build must never be read as this one's.
        File.Delete(logPath);

        ProcessStartInfo startInfo = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (string argument in plan.BuildArguments(logPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        List<string> console = [];
        // Left undisposed: a stream that closes after the drain timeout still signals it.
        CountdownEvent drained = new(2);
        using Process process = new() { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Collect(console, drained, e.Data);
        process.ErrorDataReceived += (_, e) => Collect(console, drained, e.Data);

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new DriverException("build-not-started",
                $"Could not start `dotnet build {plan.Target.ProjectFile}`: {ex.Message}", 1);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(true);
            throw new DriverException("build-timeout",
                $"dotnet build {plan.Target.ProjectFile} -c {plan.Configuration} did not finish within " +
                $"{Timeout.TotalMinutes:0} minutes and was stopped. Its log so far is {logPath}.", 1);
        }

        stopwatch.Stop();
        if (!drained.Wait(OutputDrainTimeout))
        {
            Console.Error.WriteLine(
                "geodc-ui: dotnet build exited but its console output did not close; reading the errors from its log.");
        }

        List<string> lines;
        lock (console)
        {
            lines = [.. console];
        }

        if (File.Exists(logPath))
        {
            lines.AddRange(File.ReadLines(logPath));
        }

        return new DotnetBuildResult(process.ExitCode, stopwatch.ElapsedMilliseconds, logPath,
            BuildErrors.Parse(lines));
    }

    private static void Collect(List<string> console, CountdownEvent drained, string? line)
    {
        if (line is null)
        {
            drained.Signal();
            return;
        }

        lock (console)
        {
            console.Add(line);
        }
    }
}