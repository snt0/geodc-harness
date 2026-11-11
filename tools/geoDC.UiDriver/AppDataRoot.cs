using System.IO;
using Microsoft.Win32;

namespace geoDC.UiDriver;

/// <summary>
///     Locates geoDC.App's data root the way <c>geoDC.App.Startup.RegistryApplicationDataPathProvider</c> does:
///     the <c>DataRootPath</c> registry value (current user, then machine), else <c>%LOCALAPPDATA%\geoDC</c>.
///     The app writes its rolling logs under <c>&lt;root&gt;\logs</c>. The UI driver links no geoDC assembly, so
///     this logic is duplicated here: keep the two in step.
/// </summary>
public static class AppDataRoot
{
    private const string RegistryKeyPath = @"Software\GEOsolutions\geoDC";
    private const string RegistryValueName = "DataRootPath";

    /// <summary>
    ///     The full path of the app's log directory, <c>&lt;root&gt;\logs</c>, resolved from the registry on every
    ///     call. The directory may not exist. An unreadable registry value or an invalid configured path falls back to
    ///     the default root.
    /// </summary>
    public static string LogsDirectory()
    {
        return Path.Combine(Resolve(), "logs");
    }

    private static string Resolve()
    {
        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "geoDC");
        string? configured = Read(RegistryHive.CurrentUser) ?? Read(RegistryHive.LocalMachine);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaultPath;
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return defaultPath;
        }
    }

    private static string? Read(RegistryHive hive)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(RegistryKeyPath);
            string? value = key?.GetValue(RegistryValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException
                                       or IOException)
        {
            return null;
        }
    }
}