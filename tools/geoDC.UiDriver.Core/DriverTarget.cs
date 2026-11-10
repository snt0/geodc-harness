using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     A driver-attachable application: the main geoDC app or the UI-toolkit sample gallery.
///     The sample host exists so control-level automation work (peers, template ids) can be
///     verified in isolation without setting up app state.
/// </summary>
/// <param name="Name">The <c>--target</c> value, which also names its instance file.</param>
/// <param name="ProjectDirectory">The project's directory, relative to the repository root (the working directory).</param>
/// <param name="AssemblyName">The project's assembly name, which is also its project file and executable name.</param>
/// <param name="TargetFramework">The target framework folder the build writes under <c>bin/&lt;configuration&gt;/</c>.</param>
/// <param name="OpensProject">True when a launch opens a project package (<c>--project</c>).</param>
public sealed record DriverTarget(
    string Name,
    string ProjectDirectory,
    string AssemblyName,
    string TargetFramework,
    bool OpensProject)
{
    /// <summary>The main geoDC app (<c>--target app</c>), launched with a project package.</summary>
    public static readonly DriverTarget App = new("app", "src/geoDC.App", "geoDC.App",
        "net10.0-windows10.0.17763.0", true);

    /// <summary>The UI-toolkit sample gallery (<c>--target sample</c>), launched without a project package.</summary>
    public static readonly DriverTarget Sample = new("sample", "src/geoDC.App.UI.Sample", "geoDC.App.UI.Sample",
        "net10.0-windows", false);

    /// <summary>
    ///     The sample project a launch opens when no <c>--project</c> is given, relative to the
    ///     <see cref="DriverHome" />. The UI driver is meant to open only the sample projects, unless the developer names
    ///     another package.
    /// </summary>
    public const string DefaultSampleProjectPath = "dev/sample-projects/sample.gdcproj";

    /// <summary>The build configurations the UI driver launches from, in the order <see cref="ExeCandidates" /> lists them.</summary>
    public static IReadOnlyList<string> Configurations { get; } = ["Debug", "Release"];

    /// <summary>The project file <c>rebuild</c> builds, relative to the repository root.</summary>
    public string ProjectFile => $"{ProjectDirectory}/{AssemblyName}.csproj";

    /// <summary>The executable of every configuration, relative to the repository root, whether built or not.</summary>
    public IReadOnlyList<string> ExeCandidates => [.. Configurations.Select(ExecutablePath)];

    /// <summary>The executable <c>dotnet build -c <paramref name="configuration" /></c> writes, relative to the repository root.</summary>
    public string ExecutablePath(string configuration)
    {
        return $"{ProjectDirectory}/bin/{configuration}/{TargetFramework}/{AssemblyName}.exe";
    }

    /// <summary>
    ///     The configuration whose executable was written last, or null when none is built. Ties go to the configuration
    ///     listed first.
    /// </summary>
    /// <param name="writeTimeUtc">The last write time of a file, or null when it does not exist.</param>
    public string? NewestConfiguration(Func<string, DateTime?> writeTimeUtc)
    {
        string? newest = null;
        DateTime newestWritten = DateTime.MinValue;
        foreach (string configuration in Configurations)
        {
            if (writeTimeUtc(ExecutablePath(configuration)) is { } written && (newest is null || written > newestWritten))
            {
                newest = configuration;
                newestWritten = written;
            }
        }

        return newest;
    }

    /// <summary>
    ///     The configuration whose executable is <paramref name="executablePath" />, compared as full paths and ignoring
    ///     case. Null when it is none of them, as for an executable set by <c>GEODC_APP_EXE</c>.
    /// </summary>
    public string? ConfigurationOf(string executablePath)
    {
        string full = Path.GetFullPath(executablePath);
        return Configurations.FirstOrDefault(configuration =>
            string.Equals(Path.GetFullPath(ExecutablePath(configuration)), full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolves <c>--target</c>; null means the app.</summary>
    /// <exception cref="DriverException"><c>bad-arguments</c> for any other name.</exception>
    public static DriverTarget Parse(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            null or "app" => App,
            "sample" => Sample,
            _ => throw new DriverException("bad-arguments",
                $"Option '--target' expects 'app' or 'sample' but got '{name}'.", 1)
        };
    }
}