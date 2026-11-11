using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     Runs the test-data generator CLI for <c>sample create</c>, as <c>dotnet run --project
///     tools/geoDC.TestDataGenerator.Cli</c> from the working directory, so the driver needs no reference to geoDC's
///     assemblies. Nothing the generator prints reaches the UI driver's stdout, which stays one JSON line. The output
///     is collected for the sample project's provenance and recorded in the run's
///     <see cref="SampleProjectCreateLog" />. When the run fails, its last lines also go in the error.
/// </summary>
public static class SampleProjectGenerator
{
    /// <summary>The generator project, relative to the repository root.</summary>
    public const string ProjectPath = "tools/geoDC.TestDataGenerator.Cli";

    /// <summary>
    ///     How long a run, including the build <c>dotnet run</c> does first, may take before it is stopped.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private const int FailureLineCount = 20;

    // Once the generator has exited, how long to wait for the last of its console output.
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Generates <paramref name="sampleProject" /> into <paramref name="stagingDirectory" /> and returns what it
    ///     printed, recording the command, its output, exit code and elapsed time in <paramref name="log" />.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>sample-project-generator-not-started</c> when <c>dotnet</c> cannot be started;
    ///     <c>sample-project-generator-timeout</c> when the run passes <see cref="Timeout" />;
    ///     <c>sample-project-generator-failed</c> when it exits with an error.
    /// </exception>
    public static IReadOnlyList<string> Run(DriverSampleProject sampleProject, string stagingDirectory,
        SampleProjectCreateLog log)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (string argument in (string[])["run", "--project", ProjectPath, "--"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string argument in sampleProject.GeneratorArguments(stagingDirectory))
        {
            startInfo.ArgumentList.Add(argument);
        }

        log.Event($"Running dotnet {string.Join(' ', startInfo.ArgumentList)}");
        List<string> output = [];
        // Left undisposed: a stream that closes after the drain timeout still signals it.
        CountdownEvent drained = new(2);
        using Process process = new() { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Collect(output, drained, e.Data);
        process.ErrorDataReceived += (_, e) => Collect(output, drained, e.Data);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            log.Event($"dotnet could not be started: {ex.Message}");
            throw new DriverException("sample-project-generator-not-started",
                $"Could not start `dotnet run --project {ProjectPath}`: {ex.Message}", 1);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(true);
            log.Event($"Stopped the generator after {stopwatch.ElapsedMilliseconds} ms, past the timeout.");
            lock (output)
            {
                log.Output(output);
            }

            throw new DriverException("sample-project-generator-timeout",
                $"The test-data generator did not finish within {Timeout.TotalMinutes:0} minutes and was stopped.", 1);
        }

        drained.Wait(OutputDrainTimeout);
        List<string> lines;
        lock (output)
        {
            lines = [.. output];
        }

        log.Output(lines);
        log.Event($"Generator exited with code {process.ExitCode} after {stopwatch.ElapsedMilliseconds} ms.");

        if (process.ExitCode != 0)
        {
            throw new DriverException("sample-project-generator-failed",
                $"The test-data generator exited with code {process.ExitCode}; nothing in " +
                $"{DriverSampleProject.SampleProjectDirectory} was changed. The full output is in {log.Path}.", 1,
                details: new { output = lines.TakeLast(FailureLineCount).ToArray(), log = log.Path });
        }

        return lines;
    }

    private static void Collect(List<string> output, CountdownEvent drained, string? line)
    {
        if (line is null)
        {
            drained.Signal();
            return;
        }

        lock (output)
        {
            output.Add(line);
        }
    }
}