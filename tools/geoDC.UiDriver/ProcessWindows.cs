using System.Runtime.InteropServices;

namespace geoDC.UiDriver;

/// <summary>
///     Win32 enumeration of a process's visible top-level windows.
///     <para>
///         This exists to keep UIA off the hot path. Asking UIA for the desktop's children and
///         filtering by process id makes the provider walk every top-level window on the machine,
///         reading properties cross-process on each. That was measured at 6-8 seconds per call, paid
///         again by every verb that resolves a window. <c>EnumWindows</c> answers the same question
///         in-process in microseconds, and the handles it returns convert straight to automation
///         elements via <c>FromHandle</c>, which is a single direct call rather than a search.
///     </para>
///     <para>
///         Owned windows are included: <c>EnumWindows</c> enumerates every top-level window,
///         owned or not, which is what makes detached and immersive popups visible here. Invisible
///         helper windows (WPF creates several per process) are filtered out. Message-only windows
///         are not enumerated in the first place.
///     </para>
/// </summary>
public static partial class ProcessWindows
{
    /// <summary>
    ///     The handles of the visible top-level windows, owned or not, that belong to process
    ///     <paramref name="processId" />, in the z-order <c>EnumWindows</c> reports (topmost first). Empty when the
    ///     process has none or has exited.
    /// </summary>
    public static IReadOnlyList<IntPtr> VisibleTopLevel(int processId)
    {
        List<IntPtr> handles = [];
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == (uint)processId)
            {
                handles.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}