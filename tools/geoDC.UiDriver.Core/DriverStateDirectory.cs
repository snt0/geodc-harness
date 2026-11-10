using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     The <c>.ui-driver/</c> directory in the <see cref="DriverHome" /> that holds the UI driver's state between
///     invocations: the instance files, the stored <c>tree</c> snapshots, build logs, register copies, screenshots and
///     reports. Each checkout keeps its own state. The harness's <c>.gitignore</c> excludes it.
/// </summary>
public static class DriverStateDirectory
{
    /// <summary>The directory name, relative to the driver home.</summary>
    public const string Name = ".ui-driver";

    /// <summary>The absolute path of <paramref name="parts" /> joined under <c>.ui-driver/</c> in the driver home.</summary>
    public static string Resolve(params string[] parts)
    {
        return DriverHome.Resolve(Path.Combine([Name, .. parts]));
    }
}