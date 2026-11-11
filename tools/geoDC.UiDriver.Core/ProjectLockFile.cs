using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace geoDC.UiDriver;

/// <summary>
///     The advisory lock geoDC writes beside a project package it opens: <c>&lt;package&gt;.lock</c>, JSON naming the
///     holder's user, computer and process. It is read the way <c>geoDC.Infrastructure</c>'s
///     <c>SiblingFileProjectPackageLockService</c> writes it. The UI driver links no geoDC assembly, so the format is
///     duplicated here: keep the two in step.
/// </summary>
/// <remarks>
///     A driver-launched instance that is killed or crashes leaves its lock behind, and geoDC then opens behind a
///     modal prompt that blocks UI Automation. So before it launches, the UI driver clears a lock whose holder ran on
///     this computer and is no longer running. That is the same test geoDC uses to call a lock stale. Any other lock
///     is reported.
/// </remarks>
public static class ProjectLockFile
{
    private const string Suffix = ".lock";

    /// <summary>The lock file beside <paramref name="projectPath" />.</summary>
    public static string PathFor(string projectPath)
    {
        return Path.GetFullPath(projectPath) + Suffix;
    }

    /// <summary>
    ///     Removes the lock beside <paramref name="projectPath" /> when it is stale, and reports what was there.
    /// </summary>
    public static ProjectLock ClearIfStale(string projectPath)
    {
        return ClearIfStale(projectPath, Environment.MachineName, IsProcessRunning);
    }

    /// <summary>
    ///     Removes the lock beside <paramref name="projectPath" /> when its holder ran on
    ///     <paramref name="machineName" /> and <paramref name="isProcessRunning" /> says that process has gone.
    ///     Returns the lock as found. A stale lock has been deleted by the time this returns, unless it changed while
    ///     it was being cleared, which is reported as <see cref="ProjectLockStatus.Contended" />.
    /// </summary>
    /// <remarks>
    ///     The stale lock is claimed by renaming it aside, which only one process can do, and its contents are checked
    ///     against what was read before it is deleted. A lock another session wrote in the meantime is put back
    ///     untouched, so a lock that another session still holds is never removed.
    /// </remarks>
    public static ProjectLock ClearIfStale(string projectPath, string machineName, Func<int, bool> isProcessRunning)
    {
        (ProjectLock found, string? content) = ReadWithContent(projectPath, machineName, isProcessRunning);
        if (found.Status != ProjectLockStatus.Stale)
        {
            return found;
        }

        string path = PathFor(projectPath);
        string claimed = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Move(path, claimed);
        }
        catch (FileNotFoundException)
        {
            // Cleared or released by someone else first: nothing is left to block the launch.
            return found;
        }
        catch (IOException)
        {
            return found with { Status = ProjectLockStatus.Contended };
        }

        if (string.Equals(File.ReadAllText(claimed), content, StringComparison.Ordinal))
        {
            File.Delete(claimed);
            return found;
        }

        // Another session replaced the lock after it was read; give it back rather than remove it.
        try
        {
            File.Move(claimed, path);
        }
        catch (IOException)
        {
            // A newer lock already took the path; the claimed copy stays beside it rather than be lost.
        }

        return found with { Status = ProjectLockStatus.Contended };
    }

    /// <summary>Reads the lock beside <paramref name="projectPath" /> without changing it.</summary>
    public static ProjectLock Read(string projectPath, string machineName, Func<int, bool> isProcessRunning)
    {
        return ReadWithContent(projectPath, machineName, isProcessRunning).Lock;
    }

    private static (ProjectLock Lock, string? Content) ReadWithContent(string projectPath, string machineName,
        Func<int, bool> isProcessRunning)
    {
        string path = PathFor(projectPath);
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return (ProjectLock.None, null);
        }

        return (Classify(content, machineName, isProcessRunning), content);
    }

    private static ProjectLock Classify(string content, string machineName, Func<int, bool> isProcessRunning)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            string? user = ReadString(root, "userName");
            string? machine = ReadString(root, "machineName");
            int processId = root.ValueKind == JsonValueKind.Object &&
                            root.TryGetProperty("processId", out JsonElement pid) &&
                            pid.TryGetInt32(out int value)
                ? value
                : 0;
            // geoDC reads a lock that does not name its user, computer and process as unreadable and asks the
            // operator, so the UI driver never clears one either.
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(machine) || processId <= 0)
            {
                return new ProjectLock(ProjectLockStatus.Unreadable, user, machine, processId);
            }

            ProjectLockStatus status = !string.Equals(machine, machineName, StringComparison.OrdinalIgnoreCase)
                ? ProjectLockStatus.OtherComputer
                : isProcessRunning(processId)
                    ? ProjectLockStatus.Live
                    : ProjectLockStatus.Stale;
            return new ProjectLock(status, user, machine, processId);
        }
        catch (JsonException)
        {
            return new ProjectLock(ProjectLockStatus.Unreadable, null, null, 0);
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>What the lock beside a project package says, as far as this computer can tell.</summary>
public enum ProjectLockStatus
{
    /// <summary>No lock: geoDC opens the package without asking.</summary>
    None,

    /// <summary>Held by a geoDC process on this computer that is no longer running.</summary>
    Stale,

    /// <summary>Held by a geoDC process on this computer that is still running.</summary>
    Live,

    /// <summary>Held by a session on another computer, which cannot be checked from here.</summary>
    OtherComputer,

    /// <summary>A lock file that does not name its holder.</summary>
    Unreadable,

    /// <summary>A stale lock that another session replaced or claimed while the UI driver was clearing it.</summary>
    Contended
}

/// <summary>A project package lock as found.</summary>
/// <param name="Status">Whether it can be cleared.</param>
/// <param name="UserName">The holder's user, when the lock names one.</param>
/// <param name="MachineName">The holder's computer, when the lock names one.</param>
/// <param name="ProcessId">The holder's process id, or 0.</param>
public sealed record ProjectLock(ProjectLockStatus Status, string? UserName, string? MachineName, int ProcessId)
{
    /// <summary>No lock beside the package.</summary>
    public static ProjectLock None { get; } = new(ProjectLockStatus.None, null, null, 0);

    /// <summary>True when geoDC would open the package behind a lock prompt.</summary>
    public bool BlocksLaunch => Status is ProjectLockStatus.Live or ProjectLockStatus.OtherComputer or
        ProjectLockStatus.Unreadable or ProjectLockStatus.Contended;

    /// <summary>The holder in words, for an error message.</summary>
    public string Describe()
    {
        return Status switch
        {
            ProjectLockStatus.Live => $"geoDC process {ProcessId} ({UserName}) on this computer",
            ProjectLockStatus.OtherComputer => $"{UserName} on {MachineName}",
            ProjectLockStatus.Unreadable => "a lock file that does not name its holder",
            ProjectLockStatus.Stale => $"geoDC process {ProcessId}, which is no longer running",
            ProjectLockStatus.Contended => "another session that changed the lock while it was being cleared",
            _ => "nobody"
        };
    }
}