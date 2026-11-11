using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     The driver's instance: the worktree-local record of the process the UI driver launched for a target. Later
///     invocations act only on the recorded PID, so an unrelated geoDC process with the same executable name is never
///     selected.
/// </summary>
/// <remarks>
///     The instance file outlives its process. When the app exits or crashes, the file stays, so <c>status</c> can
///     report the instance as gone and <c>rebuild</c> can launch it again with the same arguments. <c>kill</c> removes
///     the file, and the next launch replaces it. Only <see cref="TryOpen(DriverTarget, out bool)" /> decides whether
///     the recorded process is still the one that was launched.
/// </remarks>
public static class DriverInstance
{
    /// <summary>
    ///     Opens the driver's instance of <paramref name="target" />, or returns null when there is no instance file or
    ///     its process has exited or is not the one that was launched; see
    ///     <see cref="TryOpen(DriverTarget, out bool)" />. The caller disposes the returned process.
    /// </summary>
    public static Process? TryOpen(DriverTarget target)
    {
        return TryOpen(target, out _);
    }

    /// <summary>
    ///     Opens the driver's instance and reports whether it was launched in passive mode. Returns null when there is
    ///     no instance file or its process has exited. A process id since reused by another process does not match,
    ///     because its start time and executable differ from the recorded ones.
    /// </summary>
    public static Process? TryOpen(DriverTarget target, out bool passive)
    {
        passive = false;
        if (TryRead(target) is not { } instance)
        {
            return null;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(instance.Pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != instance.StartTimeUtc ||
                !string.Equals(ProcessPath(process), instance.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return null;
            }

            passive = instance.Passive;
            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The process has gone, or belongs to someone this account cannot inspect: either way it is not ours.
            process?.Dispose();
            return null;
        }
    }

    /// <summary>
    ///     Reads the instance file of <paramref name="target" /> without checking its process. Returns null when there
    ///     is none. A file that is not an instance record for this target is removed.
    /// </summary>
    public static DriverInstanceRecord? TryRead(DriverTarget target)
    {
        string path = FilePath(target);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"geodc-ui: could not read the instance file '{path}': {ex.Message}");
            return null;
        }

        DriverInstanceRecord? instance = DriverInstanceRecord.Parse(json);
        if (instance is null || !string.Equals(instance.Target, target.Name, StringComparison.Ordinal))
        {
            Delete(target);
            return null;
        }

        return instance;
    }

    /// <summary>
    ///     Records <paramref name="process" /> as the driver's instance of the target, replacing any earlier instance
    ///     file atomically.
    /// </summary>
    public static void Write(DriverTarget target, Process process, string executablePath, bool passive,
        string? projectPath)
    {
        string path = FilePath(target);
        Directory.CreateDirectory(DriverStateDirectory.Resolve());

        DriverInstanceRecord instance = new(target.Name, process.Id, process.StartTime.ToUniversalTime(),
            Path.GetFullPath(executablePath), passive, projectPath, Environment.CurrentDirectory);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, instance.ToJson());
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    ///     Removes the instance file of <paramref name="target" /> if there is one. An I/O or access failure leaves the
    ///     file in place without throwing. A later invocation validates it and replaces or removes it.
    /// </summary>
    public static void Delete(DriverTarget target)
    {
        try
        {
            File.Delete(FilePath(target));
        }
        catch (IOException)
        {
            // A later invocation will validate and replace or remove the stale instance file.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep process operations usable even when cleanup is temporarily blocked.
        }
    }

    /// <summary>
    ///     The absolute path of the instance file of <paramref name="target" />,
    ///     <c>.ui-driver/&lt;name&gt;-instance.json</c> in the driver home, whether or not it exists.
    /// </summary>
    public static string FilePath(DriverTarget target)
    {
        return DriverStateDirectory.Resolve($"{target.Name}-instance.json");
    }

    private static string ProcessPath(Process process)
    {
        return Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
    }
}