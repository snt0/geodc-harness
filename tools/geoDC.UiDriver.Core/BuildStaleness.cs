using System.IO;

namespace geoDC.UiDriver;

/// <summary>A build output file and when it was last written.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="WrittenUtc">Its last write time.</param>
public sealed record BuildArtifact(string Path, DateTime WrittenUtc);

/// <summary>Whether a running instance is older than its build.</summary>
/// <param name="Stale">True when some build output was written after the process started.</param>
/// <param name="Newest">The most recently written output newer than the process, or null when it is not stale.</param>
public sealed record BuildStaleness(bool Stale, BuildArtifact? Newest)
{
    /// <summary>The <c>warning</c> value every command adds while the driver's instance is stale.</summary>
    public const string Warning = "stale-build";

    /// <summary>The shared result for an instance that no build output post-dates: not stale, no newest artifact.</summary>
    public static BuildStaleness Fresh { get; } = new(false, null);

    /// <summary>
    ///     A process that started at <paramref name="processStartUtc" /> is stale when any of
    ///     <paramref name="artifacts" /> was written strictly after it; <see cref="Newest" /> is the latest of those.
    /// </summary>
    public static BuildStaleness Evaluate(DateTime processStartUtc, IEnumerable<BuildArtifact> artifacts)
    {
        BuildArtifact? newest = null;
        foreach (BuildArtifact artifact in artifacts)
        {
            if (artifact.WrittenUtc > processStartUtc && (newest is null || artifact.WrittenUtc > newest.WrittenUtc))
            {
                newest = artifact;
            }
        }

        return newest is null ? Fresh : new BuildStaleness(true, newest);
    }

    /// <summary>
    ///     The build outputs whose write time decides whether an instance launched from
    ///     <paramref name="executablePath" /> is stale. Files that do not exist are left out.
    /// </summary>
    /// <param name="executablePath">The executable the instance was launched from.</param>
    /// <param name="configuration">
    ///     The build configuration the executable belongs to, or null when it is not a known build output.
    /// </param>
    /// <param name="sourceRoot">The directory holding the repository's projects, one folder per assembly name.</param>
    /// <param name="otherExecutables">Executables a relaunch could pick instead, such as other configurations' builds.</param>
    /// <remarks>
    ///     <para>Three sets of files count:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             every <c>*.exe</c> and <c>*.dll</c> directly in the executable's directory: the app and each
    ///             assembly it loads, so a rebuilt dependency counts as well as the app itself;
    ///         </item>
    ///         <item>
    ///             for each of those assemblies built from a project under <paramref name="sourceRoot" />, its
    ///             compiled copy in <c>obj/&lt;configuration&gt;/&lt;framework&gt;/</c>: a build that compiled while
    ///             the running instance locked the output directory fails to copy there, and the compiled copy is then
    ///             the only newer file;
    ///         </item>
    ///         <item><paramref name="otherExecutables" />, since a relaunch takes the newest build.</item>
    ///     </list>
    ///     <para>
    ///         Satellite folders and content files are not read: they change with the assemblies that use them, and
    ///         reading them would slow every command.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<BuildArtifact> Collect(string executablePath, string? configuration,
        string sourceRoot, IEnumerable<string> otherExecutables)
    {
        List<BuildArtifact> artifacts = [];
        if (Path.GetDirectoryName(Path.GetFullPath(executablePath)) is { } outputDirectory &&
            Directory.Exists(outputDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(outputDirectory))
            {
                string extension = Path.GetExtension(file);
                bool assembly = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
                if (!assembly && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                artifacts.Add(Read(file));
                if (assembly && configuration is not null)
                {
                    AddCompiledCopies(artifacts, Path.GetFileNameWithoutExtension(file), configuration,
                        sourceRoot);
                }
            }
        }

        foreach (string other in otherExecutables)
        {
            string full = Path.GetFullPath(other);
            if (File.Exists(full) && !artifacts.Any(artifact =>
                    string.Equals(artifact.Path, full, StringComparison.OrdinalIgnoreCase)))
            {
                artifacts.Add(Read(full));
            }
        }

        return artifacts;
    }

    private static void AddCompiledCopies(List<BuildArtifact> artifacts, string assemblyName, string configuration,
        string sourceRoot)
    {
        string intermediate = Path.Combine(sourceRoot, assemblyName, "obj", configuration);
        if (!Directory.Exists(intermediate))
        {
            return;
        }

        foreach (string frameworkDirectory in Directory.EnumerateDirectories(intermediate))
        {
            string compiled = Path.Combine(frameworkDirectory, assemblyName + ".dll");
            if (File.Exists(compiled))
            {
                artifacts.Add(Read(compiled));
            }
        }
    }

    private static BuildArtifact Read(string path)
    {
        string full = Path.GetFullPath(path);
        return new BuildArtifact(full, File.GetLastWriteTimeUtc(full));
    }
}