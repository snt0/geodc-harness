namespace geoDC.UiDriver;

/// <summary>
///     Keeps a passive-launched app out of the developer's way across one UI driver step.
///     <para>
///         Opening a window without activation is not enough on its own. Steps can still pull the app forward:
///         a UIA <c>SetFocus</c> fallback, a combo expand, a popup restoring keyboard focus, or a WebView2 chart
///         claiming focus as it loads. Instead of handling each case, the guard notes who owned the foreground
///         before the step, hands it back if the app took it, and puts the app's windows (including popups
///         opened by the step) under everything else. Screenshots are unaffected, because PrintWindow captures
///         occluded windows.
///     </para>
///     <para>
///         The guard only runs in passive sessions. An active launch may come forward and is never pushed back.
///         Verbs that need the foreground (<c>key</c>) refuse to run in a passive session, so no step there has a
///         legitimate reason to take it.
///     </para>
/// </summary>
public sealed class ForegroundGuard
{
    private readonly IntPtr _before;
    private readonly GeoDcSession _session;

    private ForegroundGuard(GeoDcSession session, IntPtr before)
    {
        _session = session;
        _before = before;
    }

    /// <summary>Starts guarding a step, or returns null when the session is not passive.</summary>
    public static ForegroundGuard? Begin(GeoDcSession session)
    {
        return session.Passive ? new ForegroundGuard(session, WindowActivation.Foreground()) : null;
    }

    /// <summary>Restores the foreground if the step took it, and sends the app's windows to the back.</summary>
    public void End(string verb)
    {
        int processId = _session.Process.Id;
        if (WindowActivation.OwningProcessId(_before) == processId)
        {
            return;
        }

        if (_before != IntPtr.Zero && WindowActivation.OwningProcessId(WindowActivation.Foreground()) == processId)
        {
            bool restored = WindowActivation.Activate(_before);
            DriverTrace.Write(verb, restored ? "restored" : "restore-failed");

            // stderr, not the JSON line: this is a diagnostic for whoever is hunting the focus thief.
            Console.Error.WriteLine(restored
                ? $"geodc-ui: '{verb}' brought geoDC to the foreground; returned it to the previous window."
                : $"geodc-ui: '{verb}' brought geoDC to the foreground and the previous window could not be restored.");
        }

        foreach (IntPtr window in ProcessWindows.VisibleTopLevel(processId))
        {
            WindowActivation.SendToBack(window);
        }

        DriverTrace.Write(verb, "sent-to-back");
    }
}