using System.Diagnostics;
using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     Compares the driver's instance with its build output on disk (see <see cref="BuildStaleness.Collect" /> for the
///     files that count), so a command never silently drives an app older than the code it is meant to verify.
/// </summary>
public static class StaleBuildCheck
{
    /// <summary>How the driver's instance compares with its build output.</summary>
    public static BuildStaleness Evaluate(DriverTarget target, DriverInstanceRecord instance)
    {
        IReadOnlyList<BuildArtifact> artifacts = BuildStaleness.Collect(instance.ExecutablePath,
            target.ConfigurationOf(instance.ExecutablePath),
            Path.GetFullPath(Path.GetDirectoryName(target.ProjectDirectory) ?? string.Empty),
            target.ExeCandidates);
        return BuildStaleness.Evaluate(instance.StartTimeUtc, artifacts);
    }

    /// <summary>
    ///     <see cref="BuildStaleness.Warning" /> when the driver's instance of the target is running and stale,
    ///     otherwise null. A stale instance is also described on stderr. An unknown target, a missing or dead instance,
    ///     or unreadable build output gives no warning instead of an exception, so the command it decorates still
    ///     reports its own result.
    /// </summary>
    public static string? WarningFor(string? targetName)
    {
        try
        {
            DriverTarget target = DriverTarget.Parse(targetName);
            if (DriverInstance.TryRead(target) is not { } instance)
            {
                return null;
            }

            using (Process? process = DriverInstance.TryOpen(target))
            {
                if (process is null)
                {
                    return null;
                }
            }

            BuildStaleness staleness = Evaluate(target, instance);
            if (staleness.Newest is not { } newest)
            {
                return null;
            }

            Console.Error.WriteLine(
                $"geodc-ui: the driver's {target.Name} instance (pid {instance.Pid}, started " +
                $"{instance.StartTimeUtc:O}) is older than its build: {newest.Path} was written " +
                $"{newest.WrittenUtc:O}. Run `rebuild` to relaunch it.");
            return BuildStaleness.Warning;
        }
        catch (DriverException)
        {
            // Only an invalid --target throws one, and the command itself fails on that and says so.
            return null;
        }
        catch (Exception ex)
        {
            // The check decorates another command's result; it must never replace it.
            Console.Error.WriteLine($"geodc-ui: could not compare the driver's instance with its build: {ex.Message}");
            return null;
        }
    }
}