using System.IO;

namespace geoDC.UiDriver;

/// <summary>
///     The Shore Control Register a UI driver launch gives geoDC, so a UI driver session does not read or change the
///     developer's own register. A sample project's register sits beside it as <c>&lt;name&gt;-register.sqlite</c>
///     (<c>sample create</c> writes it, see <see cref="DriverSampleProject" />). Each launch copies it into
///     <c>.ui-driver/</c>, so any change the session makes to the register is thrown away at the next launch. A
///     package without a register gets an empty one.
/// </summary>
public static class DriverRegister
{
    /// <summary>The configuration key geoDC reads the register's connection string from.</summary>
    public const string ConfigurationKey = "ConnectionStrings:ShoreControlStore";

    /// <summary>The register that belongs beside <paramref name="projectPath" />.</summary>
    public static string SampleProjectRegisterPathFor(string projectPath)
    {
        string fullPath = Path.GetFullPath(projectPath);
        return Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(fullPath) + "-register.sqlite");
    }

    /// <summary>
    ///     Replaces the working register at <paramref name="workingPath" /> with a copy of the sample project's
    ///     register, or removes it so geoDC creates an empty one. Returns the command-line argument that points geoDC
    ///     at it.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>register-busy</c> (exit code 3) when the working copy is still open, by an instance the UI driver did not
    ///     launch.
    /// </exception>
    public static string Prepare(string projectPath, string workingPath)
    {
        string sampleRegister = SampleProjectRegisterPathFor(projectPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(workingPath) ?? ".");
            foreach (string suffix in (string[])["", "-wal", "-shm", "-journal"])
            {
                File.Delete(workingPath + suffix);
            }

            if (File.Exists(sampleRegister))
            {
                File.Copy(sampleRegister, workingPath);
            }
        }
        catch (IOException exception)
        {
            throw new DriverException("register-busy",
                $"Could not prepare the UI driver's Shore Control Register at {workingPath}: {exception.Message} " +
                "Close the geoDC instance using it, or run `geodc-ui kill`, then launch again.", 3);
        }

        return $"--{ConfigurationKey}=Data Source={workingPath}";
    }
}