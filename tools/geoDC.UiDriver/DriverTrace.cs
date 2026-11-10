namespace geoDC.UiDriver;

/// <summary>
///     Timestamped stderr trace of UI driver steps, showing which call brought the app to the foreground. Times are
///     local wall-clock with milliseconds, matching the app's Serilog file. Opt-in through
///     <see cref="EnvironmentVariable" />: several lines per step would otherwise flood the output for a
///     diagnostic that is only wanted when hunting a focus thief. Disabled, every call is a no-op.
/// </summary>
public static class DriverTrace
{
    /// <summary>Set to <c>1</c> to turn the trace on.</summary>
    public const string EnvironmentVariable = "GEODC_UI_TRACE";

    private static int? _appProcessId;

    /// <summary>Starts tracing against the given app process, if the trace was requested.</summary>
    public static void Enable(int appProcessId)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal))
        {
            _appProcessId = appProcessId;
        }
    }

    /// <summary>Writes one trace line naming the phase and who currently owns the foreground.</summary>
    public static void Write(string verb, string phase)
    {
        if (_appProcessId is not { } appProcessId)
        {
            return;
        }

        IntPtr foreground = WindowActivation.Foreground();
        string owner = WindowActivation.OwningProcessId(foreground) == appProcessId ? "geoDC" : "other";
        Console.Error.WriteLine(
            $"ui-driver-trace {DateTime.Now:HH:mm:ss.fff} {verb} {phase} foreground=0x{foreground.ToInt64():X8} ({owner})");
    }
}