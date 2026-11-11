using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     The folder that holds what the UI driver keeps for itself: its <c>.ui-driver/</c> state, the sample projects
///     under <c>dev/sample-projects/</c> and the scripts under <c>dev/ui-scripts/</c>. The harness's <c>dev/ui.ps1</c>
///     sets <see cref="EnvironmentVariable" /> to the harness root, so geoDC's tree stays clean. Without it, the home
///     is the working directory.
///     <para>
///         The working directory is still the geoDC repository root. Building and launching the app, reading its
///         strings files for <c>source</c>, and running the test-data generator for <c>sample create</c> all resolve
///         against it.
///     </para>
/// </summary>
public static class DriverHome
{
    /// <summary>The environment variable that names the driver home.</summary>
    public const string EnvironmentVariable = "GEODC_UI_HOME";

    /// <summary>The driver home of this process, as an absolute path.</summary>
    public static string Directory =>
        Locate(Environment.GetEnvironmentVariable(EnvironmentVariable), Environment.CurrentDirectory);

    /// <summary>
    ///     The driver home for a <see cref="EnvironmentVariable" /> value and a working directory: the configured
    ///     folder, relative to <paramref name="workingDirectory" /> when it is not rooted, or the working directory when
    ///     nothing is configured.
    /// </summary>
    public static string Locate(string? configured, string workingDirectory)
    {
        return string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(workingDirectory)
            : Path.GetFullPath(configured, Path.GetFullPath(workingDirectory));
    }

    /// <summary>The absolute path of <paramref name="path" />, resolved against <see cref="Directory" /> when relative.</summary>
    public static string Resolve(string path)
    {
        return Path.GetFullPath(path, Directory);
    }

    /// <summary>
    ///     Reads the text of <paramref name="path" />, resolved by <see cref="Resolve" />. <c>do --file</c>,
    ///     <c>run</c> and <c>rebuild --then</c> read their scripts through it.
    /// </summary>
    public static string ReadAllText(string path)
    {
        return File.ReadAllText(Resolve(path));
    }
}