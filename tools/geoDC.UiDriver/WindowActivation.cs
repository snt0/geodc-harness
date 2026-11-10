using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver;

/// <summary>
///     Brings another process's window to the foreground so <c>SendInput</c> keystrokes land in it.
///     <para>
///         UIA's <c>SetFocus</c> and a bare <c>SetForegroundWindow</c> are not enough here. Windows only
///         lets the process that already owns the foreground hand it over, and the UI driver is a console
///         process launched from a terminal that keeps it. Attaching this thread's input queue to the
///         current foreground thread (and to the target's) lifts that restriction for the duration of the
///         call. This is the standard workaround. Without it the UI driver could only type into a window it
///         had been handed the foreground for.
///     </para>
/// </summary>
public static partial class WindowActivation
{
    private const int SwRestore = 9;
    private const int SwShow = 5;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    /// <summary>Activates the window and reports whether it ended up in the foreground.</summary>
    public static bool Activate(AutomationElement window)
    {
        return Activate(window.Properties.NativeWindowHandle.ValueOrDefault);
    }

    /// <summary>Activates the window handle and reports whether it ended up in the foreground.</summary>
    public static bool Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(hwnd, out _);
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

        bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread &&
                                  AttachThreadInput(currentThread, foregroundThread, true);
        bool attachedTarget = targetThread != 0 && targetThread != currentThread &&
                              AttachThreadInput(currentThread, targetThread, true);
        try
        {
            ShowWindow(hwnd, IsIconic(hwnd) ? SwRestore : SwShow);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>The window that currently owns the foreground, or <see cref="IntPtr.Zero" />.</summary>
    public static IntPtr Foreground()
    {
        return GetForegroundWindow();
    }

    /// <summary>Id of the process that owns the window; 0 when the handle is gone.</summary>
    public static int OwningProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(hwnd, out uint processId);
        return (int)processId;
    }

    /// <summary>Moves the window to the bottom of the z-order without activating, moving, or resizing it.</summary>
    public static void SendToBack(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }
    }

    /// <summary>
    ///     Win32 enabled state. A window shown with <c>ShowDialog</c> disables its owner here while
    ///     leaving the owner's WPF/UIA <c>IsEnabled</c> true, so this is the only reading that tells a
    ///     modal-blocked window from one that can accept input.
    /// </summary>
    public static bool IsEnabled(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && IsWindowEnabled(hwnd);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowEnabled(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo,
        [MarshalAs(UnmanagedType.Bool)] bool doAttach);
}