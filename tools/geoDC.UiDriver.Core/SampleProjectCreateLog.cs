using System.Globalization;
using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     The diagnostic record of one <c>sample create</c> run, saved as
///     <c>.ui-driver/sample-projects/&lt;name&gt;-create.log</c>. It holds when the run started, the generator command
///     and everything it printed, its exit code and elapsed time, and what was installed or why the run failed. The
///     driver's stdout carries only the one-line result, so this file is where you diagnose a run. Each run replaces
///     the previous run's file.
/// </summary>
public sealed class SampleProjectCreateLog
{
    private readonly List<string> _lines = [];
    private readonly TimeProvider _time;

    /// <summary>
    ///     Starts the record of a run that creates <paramref name="sampleProject" />, saved to
    ///     <c>.ui-driver/sample-projects/&lt;name&gt;-create.log</c> under the driver home, with its opening
    ///     event stamped.
    /// </summary>
    /// <param name="sampleProject">The sample project being created; its name and preset head the record.</param>
    /// <param name="time">The clock that stamps events; null for the system clock.</param>
    public SampleProjectCreateLog(DriverSampleProject sampleProject, TimeProvider? time = null)
        : this(sampleProject,
            DriverStateDirectory.Resolve("sample-projects", $"{sampleProject.Name}-create.log"), time)
    {
    }

    /// <summary>
    ///     Starts the record of a run that creates <paramref name="sampleProject" />, saved to
    ///     <paramref name="path" />, with its opening event stamped. Nothing is written until <see cref="Save" />.
    /// </summary>
    /// <param name="sampleProject">The sample project being created; its name and preset head the record.</param>
    /// <param name="path">The file <see cref="Save" /> writes, used as given.</param>
    /// <param name="time">The clock that stamps events; null for the system clock.</param>
    public SampleProjectCreateLog(DriverSampleProject sampleProject, string path, TimeProvider? time = null)
    {
        Path = path;
        _time = time ?? TimeProvider.System;
        Event($"sample create {sampleProject.Name} (preset {sampleProject.Preset})");
    }

    /// <summary>The absolute path the record is saved to.</summary>
    public string Path { get; }

    /// <summary>The record so far, one event or output line per entry.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Adds an event line, stamped with the current UTC time.</summary>
    public void Event(string message)
    {
        _lines.Add($"{_time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture)} {message}");
    }

    /// <summary>Adds what an external process printed, indented beneath the event that ran it.</summary>
    public void Output(IEnumerable<string> lines)
    {
        _lines.AddRange(lines.Select(line => $"    {line}"));
    }

    /// <summary>Writes the record to <see cref="Path" />, replacing an earlier run's.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllLines(Path, _lines);
    }
}