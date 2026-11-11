using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace geoDC.UiDriver;

/// <summary>
///     Connection to the driver's instance: the geoDC process recorded in the worktree's instance file. The app is
///     single-instance (mutex), but unrelated app or sample processes are ignored. It works purely across the process
///     boundary (UIA and Win32) and links no geoDC assembly.
/// </summary>
public sealed partial class GeoDcSession : IDisposable
{
    /// <summary>
    ///     Mirrors <c>geoDC.App.UI.Controls.BackgroundLaunch.EnvironmentVariable</c>. The UI driver links no geoDC
    ///     assembly, so the name is duplicated here.
    /// </summary>
    private const string BackgroundLaunchVariable = "GEODC_BACKGROUND_LAUNCH";

    private GeoDcSession(DriverTarget target, UIA3Automation automation, Process process, bool launched,
        string? projectPath, bool passive)
    {
        Target = target;
        Automation = automation;
        Process = process;
        Launched = launched;
        ProjectPath = projectPath;
        Passive = passive;
    }

    /// <summary>The application this session is connected to.</summary>
    public DriverTarget Target { get; }

    /// <summary>The UIA3 automation instance every element read goes through; owned and disposed by the session.</summary>
    public UIA3Automation Automation { get; }

    /// <summary>
    ///     The driver's instance process; owned and disposed by the session. Call <c>Refresh</c> before reading live
    ///     state.
    /// </summary>
    public Process Process { get; }

    /// <summary>True when this invocation started the process; false when it attached to a running instance.</summary>
    public bool Launched { get; }

    /// <summary>
    ///     The full path of the project package this invocation launched the process with, or null when it attached to
    ///     an existing instance or the target opens no package.
    /// </summary>
    public string? ProjectPath { get; }

    /// <summary>
    ///     True when the driver's instance was launched with <c>--passive</c>: its windows never take the
    ///     foreground, <see cref="ForegroundGuard" /> hands it back after each step if anything does, and verbs
    ///     that need the foreground refuse to run. An active launch also starts behind the developer's windows,
    ///     but may then come forward and is never pushed back, so the developer can watch the run.
    /// </summary>
    public bool Passive { get; }

    /// <summary>
    ///     Connects to the driver's instance of <paramref name="target" /> (the app when null), or, when there is none
    ///     and <paramref name="allowLaunch" /> is true, launches the newest build of it as <see cref="Launch" /> does.
    ///     Attaching to a running instance keeps the passive mode it was launched with and ignores the launch options.
    /// </summary>
    /// <param name="target">The application to attach to; null for <see cref="DriverTarget.App" />.</param>
    /// <param name="allowLaunch">True to launch an instance when the driver has none.</param>
    /// <param name="projectOverride">
    ///     The package a launch opens instead of the default sample project; null for the default.
    /// </param>
    /// <param name="launchTimeout">How long a launch waits for the main window; null for 30 seconds.</param>
    /// <param name="passive">True to launch the instance in passive mode (see <see cref="Passive" />).</param>
    /// <exception cref="DriverException">
    ///     <c>not-running</c> (exit code 3) when the driver has no running instance and launching is not allowed;
    ///     <c>app-not-found</c> (exit code 3) when there is no build to launch; or any failure of <see cref="Launch" />.
    /// </exception>
    public static GeoDcSession Attach(DriverTarget? target = null, bool allowLaunch = false,
        string? projectOverride = null, TimeSpan? launchTimeout = null, bool passive = false)
    {
        target ??= DriverTarget.App;
        Process? process = DriverInstance.TryOpen(target, out bool instancePassive);
        if (process is not null)
        {
            return new GeoDcSession(target, new UIA3Automation(), process, false, null, instancePassive);
        }

        if (!allowLaunch)
        {
            throw new DriverException("not-running",
                $"No driver-owned {target.Name} instance found. Re-run `geodc-ui attach --launch` " +
                (target.OpensProject ? "to start it on the default sample project." : "to start it."),
                3);
        }

        return Launch(target, ResolveExe(target), projectOverride, launchTimeout ?? TimeSpan.FromSeconds(30), passive);
    }

    /// <summary>
    ///     All top-level windows of the geoDC process. This is the default search scope, so open typed
    ///     popups are visible without flags. Main window first, then the rest in z-order.
    ///     <para>
    ///         The windows are enumerated through Win32 (see <see cref="ProcessWindows" />), not by asking
    ///         UIA for the desktop's children. The UIA route walks every window on the machine and costs
    ///         6-8 s per call, paid again by every verb that resolves a window. <c>EnumWindows</c> also
    ///         returns owned windows directly, so there are no owner chains to walk. The desktop's UIA
    ///         children are only the <em>unowned</em> windows, and every detached or immersive popup sets
    ///         <c>Owner</c>, so that query alone finds only the main window while popups are open.
    ///     </para>
    /// </summary>
    public AutomationElement[] TopLevelWindows()
    {
        Process.Refresh();
        IntPtr main = Process.MainWindowHandle;

        List<AutomationElement> windows = [];
        foreach (IntPtr handle in ProcessWindows.VisibleTopLevel(Process.Id).OrderByDescending(h => h == main))
        {
            if (FromHandle(handle) is { } window)
            {
                windows.Add(window);
            }
        }

        return [.. windows];
    }

    /// <summary>
    ///     Wraps a window handle as an automation element. Returns null for handles UIA cannot or should
    ///     not expose, such as a window closing mid-enumeration or a helper HWND that is not a real window.
    /// </summary>
    private AutomationElement? FromHandle(IntPtr handle)
    {
        try
        {
            AutomationElement element = Automation.FromHandle(handle);
            return element.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Window
                ? element
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Resolves the single window a window-scoped verb acts on. Selector forms: <c>null</c> or
    ///     <c>main</c> (the instance's main window), <c>title:&lt;substring&gt;</c> (case-insensitive), and
    ///     <c>handle:0x…</c> (the handle <c>tree</c> prints, accepted back as printed). Popups are separate
    ///     top-level windows, so this is how a detached or immersive popup is addressed for capture or
    ///     keyboard input.
    /// </summary>
    public AutomationElement ResolveWindow(string? selector)
    {
        if (selector is null || string.Equals(selector, "main", StringComparison.OrdinalIgnoreCase))
        {
            return MainWindow();
        }

        AutomationElement[] windows = TopLevelWindows();
        List<AutomationElement> matches;
        string criterion;

        if (TryStrip(selector, "title:", out string fragment))
        {
            if (fragment.Length == 0)
            {
                throw new DriverException("bad-arguments", "Selector 'title:' needs a substring to match.", 1);
            }

            criterion = $"a title containing '{fragment}'";
            matches =
                [.. windows.Where(window => (window.Properties.Name.ValueOrDefault ?? string.Empty)
                    .Contains(fragment, StringComparison.OrdinalIgnoreCase))];
        }
        else if (TryStrip(selector, "handle:", out string rawHandle))
        {
            IntPtr handle = ParseHandle(rawHandle);
            criterion = $"handle 0x{handle:X8}";
            matches = [.. windows.Where(window => window.Properties.NativeWindowHandle.ValueOrDefault == handle)];
        }
        else
        {
            throw new DriverException("bad-arguments",
                $"Option '--window' expects 'main', 'title:<substring>', or 'handle:0x…' but got '{selector}'.", 1);
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new DriverException("window-not-found",
                $"No geoDC window with {criterion}.", 2, DescribeWindows(windows)),
            _ => throw new DriverException("ambiguous-match",
                $"{matches.Count} windows have {criterion}; use a longer fragment or handle:0x….", 2,
                DescribeWindows(matches))
        };
    }

    /// <summary>
    ///     The window that can actually accept keyboard input, following the modal chain down from
    ///     <paramref name="window" />. A popup shown modally disables its owner, so a chord aimed at the
    ///     main window while one is open would be swallowed. Following the chain lets
    ///     <c>key --keys Escape</c> close a popup without the script having to name it.
    /// </summary>
    public AutomationElement ResolveInputTarget(AutomationElement window)
    {
        AutomationElement current = window;

        // Bounded: a modal over a modal is already unusual, and this must not loop on a cycle.
        for (int depth = 0; depth < 4; depth++)
        {
            if (WindowActivation.IsEnabled(current.Properties.NativeWindowHandle.ValueOrDefault))
            {
                return current;
            }

            AutomationElement? modal = null;
            try
            {
                modal = current
                    .FindAllChildren(cf => cf
                        .ByControlType(FlaUI.Core.Definitions.ControlType.Window)
                        .And(cf.ByProcessId(Process.Id)))
                    .FirstOrDefault(child =>
                        WindowActivation.IsEnabled(child.Properties.NativeWindowHandle.ValueOrDefault));
            }
            catch (Exception)
            {
                // An unreadable child list just means the current window is kept.
            }

            if (modal is null)
            {
                return current;
            }

            current = modal;
        }

        return current;
    }

    /// <summary>
    ///     Window identity in the same shape <c>tree</c> prints, so a candidate list can be pasted
    ///     straight back into <c>--window handle:…</c>.
    /// </summary>
    public static string DescribeWindow(AutomationElement window)
    {
        string title = window.Properties.Name.ValueOrDefault ?? string.Empty;
        IntPtr handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        string className = window.Properties.ClassName.ValueOrDefault ?? string.Empty;
        return $"\"{title}\" handle=0x{handle:X8} class={className}";
    }

    /// <summary>
    ///     The driver's instance's main window. It is resolved straight from its handle, with no enumeration or
    ///     search, because every element verb starts here and it is the most frequent call in the UI driver.
    /// </summary>
    public AutomationElement MainWindow()
    {
        Process.Refresh();
        IntPtr handle = Process.MainWindowHandle;

        if (handle != IntPtr.Zero && FromHandle(handle) is { } byHandle)
        {
            return byHandle;
        }

        return TopLevelWindows().FirstOrDefault() ??
               throw new DriverException("no-windows", "The geoDC process has no top-level windows.", 3);
    }

    /// <inheritdoc />
    /// <remarks>Releases the automation instance and the process handle; the application keeps running.</remarks>
    public void Dispose()
    {
        Automation.Dispose();
        Process.Dispose();
    }

    private static string[] DescribeWindows(IEnumerable<AutomationElement> windows)
    {
        return [.. windows.Select(DescribeWindow)];
    }

    private static bool TryStrip(string selector, string prefix, out string remainder)
    {
        if (selector.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = selector[prefix.Length..].Trim();
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    private static IntPtr ParseHandle(string raw)
    {
        bool hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        string digits = hex ? raw[2..] : raw;
        if (digits.Length > 0 && long.TryParse(digits,
                hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long value))
        {
            return new IntPtr(value);
        }

        throw new DriverException("bad-arguments",
            $"Selector 'handle:' expects the hex handle printed by `tree` (e.g. handle:0x000A1234) but got '{raw}'.",
            1);
    }
}