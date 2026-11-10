using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver;

/// <summary>
///     What <see cref="Screenshotter.CaptureWindow" /> wrote.
/// </summary>
/// <param name="Path">The PNG file.</param>
/// <param name="Width">Written image width in pixels, after any downscale.</param>
/// <param name="Height">Written image height in pixels, after any downscale.</param>
/// <param name="OriginalWidth">Captured region width in surface pixels, before any downscale.</param>
/// <param name="OriginalHeight">Captured region height in surface pixels, before any downscale.</param>
/// <param name="Scale">Output pixels per captured pixel; 1 when not downscaled.</param>
/// <param name="PadPixels">The <c>--pad</c> applied to each side, in physical pixels, before clamping to the window.</param>
/// <param name="Highlights">Where each requested highlight landed, in request order.</param>
public sealed record ScreenshotCapture(
    string Path,
    int Width,
    int Height,
    int OriginalWidth,
    int OriginalHeight,
    double Scale,
    int PadPixels,
    IReadOnlyList<HighlightPlacement> Highlights);

/// <summary>
///     Captures a top-level window via <c>PrintWindow(PW_RENDERFULLCONTENT)</c>. The image comes from
///     the compositor, so it works while the window is occluded and includes DirectX and WebView2
///     surfaces as pixels. CopyFromScreen is not used. All coordinates are physical pixels (the UI
///     driver is PerMonitorV2 DPI-aware, so Win32 and UIA both report physical pixels to it).
///     Cropping, downscaling and highlight outlines are applied to the captured pixels only. Nothing
///     is drawn into the app.
/// </summary>
public static partial class Screenshotter
{
    private const uint PwRenderFullContent = 0x00000002;
    private const double DipsPerInch = 96.0;

    /// <summary>Highlight outline colour: magenta, which no geoDC theme uses, so it is clearly an annotation.</summary>
    private static readonly System.Drawing.Color HighlightColor = System.Drawing.Color.FromArgb(255, 0, 255);

    /// <summary>
    ///     Captures <paramref name="window" />, crops it to <paramref name="windowRelativeCrop" /> (grown by
    ///     <see cref="ScreenshotOptions.PadDips" />), downscales it to <see cref="ScreenshotOptions.MaxWidth" />,
    ///     outlines <paramref name="highlights" />, and writes a PNG.
    /// </summary>
    /// <param name="window">The top-level window to capture.</param>
    /// <param name="windowRelativeCrop">Window-relative physical pixels, or null for the whole window.</param>
    /// <param name="options">Padding and output width; its highlight ids are already resolved into
    ///     <paramref name="highlights" />.</param>
    /// <param name="highlights">Elements to outline, in the order to report them.</param>
    /// <param name="outPath">
    ///     Destination PNG, relative to the <see cref="DriverHome" /> when not rooted, or null for a timestamped file
    ///     under <c>.ui-driver/screenshots</c>. The capture reports the path as given.
    /// </param>
    /// <exception cref="DriverException">
    ///     <c>no-window-handle</c>, <c>capture-failed</c>, or <c>region-out-of-bounds</c> when the crop misses the window.
    /// </exception>
    public static ScreenshotCapture CaptureWindow(AutomationElement window,
        System.Drawing.Rectangle? windowRelativeCrop, ScreenshotOptions options,
        IReadOnlyList<HighlightTarget> highlights, string? outPath)
    {
        IntPtr hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd == IntPtr.Zero)
        {
            throw new DriverException("no-window-handle", "Target window has no native window handle.", 2);
        }

        if (!GetWindowRect(hwnd, out Rect32 windowRect))
        {
            throw new DriverException("capture-failed", "GetWindowRect failed for the target window.", 1);
        }

        int physicalWidth = windowRect.Right - windowRect.Left;
        int physicalHeight = windowRect.Bottom - windowRect.Top;
        if (physicalWidth <= 0 || physicalHeight <= 0)
        {
            throw new DriverException("capture-failed", "Target window has an empty bounding rectangle.", 2);
        }

        // PrintWindow renders the window's own backing surface. For a window that is not
        // per-monitor DPI aware, that surface is smaller than the physical (DWM-scaled) rect,
        // so capture at surface size and translate physical-pixel crops into surface pixels.
        double scale = SurfaceScale(hwnd);
        System.Drawing.Size surface = new((int)Math.Round(physicalWidth * scale),
            (int)Math.Round(physicalHeight * scale));

        BgraImage image = PrintWindowToImage(hwnd, surface.Width, surface.Height);

        int padPixels = 0;
        System.Drawing.Rectangle source = new(System.Drawing.Point.Empty, surface);
        if (windowRelativeCrop is { } physicalCrop)
        {
            if (options.PadDips > 0)
            {
                padPixels = ScreenshotGeometry.PadPixels(options.PadDips, PhysicalPixelsPerDip(hwnd));
                physicalCrop = ScreenshotGeometry.PadCrop(physicalCrop, padPixels,
                    new System.Drawing.Size(physicalWidth, physicalHeight));
            }

            source = ScreenshotGeometry.SurfaceCrop(physicalCrop, scale, surface) ??
                     throw new DriverException("region-out-of-bounds",
                         $"Requested region {physicalCrop} does not intersect the window " +
                         $"(0,0 {physicalWidth}x{physicalHeight} physical pixels).", 2);
            image = image.Crop(source);
        }

        System.Drawing.Size output = ScreenshotGeometry.OutputSize(image.Size, options.MaxWidth);
        BgraImage written = image.Downscale(output.Width, output.Height);

        System.Drawing.Point windowOrigin = new(windowRect.Left, windowRect.Top);
        List<HighlightPlacement> placements = [];
        foreach (HighlightTarget highlight in highlights)
        {
            HighlightPlacement placement =
                ScreenshotGeometry.PlaceHighlight(highlight, windowOrigin, scale, source, output);
            if (placement.Bounds is { } bounds)
            {
                written.DrawRing(ScreenshotGeometry.OutlineRect(bounds, output), ScreenshotGeometry.OutlineThickness,
                    HighlightColor);
            }

            placements.Add(placement);
        }

        string path = outPath ?? DefaultArtifactPath();
        string fullPath = DriverHome.Resolve(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        BitmapSource bitmap = BitmapSource.Create(written.Width, written.Height, 96, 96, PixelFormats.Bgra32, null,
            written.Pixels, written.Stride);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(fullPath))
        {
            encoder.Save(stream);
        }

        return new ScreenshotCapture(path, written.Width, written.Height, image.Width, image.Height,
            ScreenshotGeometry.ScaleFactor(image.Size, output), padPixels, placements);
    }

    /// <summary>Screen-physical-pixel element bounds converted to window-relative pixels.</summary>
    public static System.Drawing.Rectangle ElementCrop(AutomationElement window, AutomationElement element)
    {
        IntPtr hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (!GetWindowRect(hwnd, out Rect32 windowRect))
        {
            throw new DriverException("capture-failed", "GetWindowRect failed for the target window.", 1);
        }

        System.Drawing.Rectangle bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new DriverException("element-not-visible",
                "Element has an empty bounding rectangle (offscreen or collapsed); nothing to capture.", 2);
        }

        return new System.Drawing.Rectangle(bounds.X - windowRect.Left, bounds.Y - windowRect.Top, bounds.Width,
            bounds.Height);
    }

    /// <summary>
    ///     Ratio of the window's backing-surface pixels to physical screen pixels: 1.0 for a per-monitor-aware
    ///     window, and windowDpi/monitorDpi otherwise.
    /// </summary>
    private static double SurfaceScale(IntPtr hwnd)
    {
        uint windowDpi = GetDpiForWindow(hwnd);
        uint monitorDpi = MonitorDpi(hwnd);
        return windowDpi == 0 || monitorDpi == 0 ? 1.0 : windowDpi / (double)monitorDpi;
    }

    /// <summary>
    ///     Physical screen pixels per device-independent pixel of the app: the monitor's effective DPI over 96, which
    ///     holds whether the window scales itself (per-monitor aware) or DWM scales it. Falls back to the window's DPI,
    ///     then to 1.0.
    /// </summary>
    private static double PhysicalPixelsPerDip(IntPtr hwnd)
    {
        uint monitorDpi = MonitorDpi(hwnd);
        if (monitorDpi != 0)
        {
            return monitorDpi / DipsPerInch;
        }

        uint windowDpi = GetDpiForWindow(hwnd);
        return windowDpi != 0 ? windowDpi / DipsPerInch : 1.0;
    }

    /// <summary>The effective DPI of the monitor nearest the window, or 0 when it cannot be read.</summary>
    private static uint MonitorDpi(IntPtr hwnd)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor == IntPtr.Zero ||
            GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint monitorDpiX, out _) != 0)
        {
            return 0;
        }

        return monitorDpiX;
    }

    private static BgraImage PrintWindowToImage(IntPtr hwnd, int width, int height)
    {
        IntPtr memoryDc = CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero)
        {
            throw new DriverException("capture-failed", "CreateCompatibleDC failed.", 1);
        }

        IntPtr dib = IntPtr.Zero;
        try
        {
            BitmapInfo info = BitmapInfo.ForTopDown32Bpp(width, height);
            dib = CreateDIBSection(memoryDc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new DriverException("capture-failed", "CreateDIBSection failed.", 1);
            }

            IntPtr previous = SelectObject(memoryDc, dib);
            bool printed = PrintWindow(hwnd, memoryDc, PwRenderFullContent);
            SelectObject(memoryDc, previous);
            if (!printed)
            {
                throw new DriverException("capture-failed", "PrintWindow(PW_RENDERFULLCONTENT) failed.", 1);
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // Some composed surfaces leave alpha at 0. Forcing it opaque keeps a PNG from coming out
            // transparent where content rendered.
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }

            return new BgraImage(width, height, pixels);
        }
        finally
        {
            if (dib != IntPtr.Zero)
            {
                DeleteObject(dib);
            }

            DeleteDC(memoryDc);
        }
    }

    private static string DefaultArtifactPath()
    {
        string fileName = $"screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png";
        return Path.Combine(".ui-driver", "screenshots", fileName);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;

        public static BitmapInfo ForTopDown32Bpp(int width, int height)
        {
            return new BitmapInfo
            {
                Size = Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            };
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits,
        IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);
}