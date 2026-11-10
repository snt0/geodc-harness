using System.Drawing;

namespace geoDC.UiDriver;

/// <summary>
///     An element to outline in a capture.
/// </summary>
/// <param name="ElementId">The AutomationId the caller named.</param>
/// <param name="ScreenBounds">The UIA BoundingRectangle: physical screen pixels, empty when offscreen or collapsed.</param>
/// <param name="InCapturedWindow">
///     Whether the element belongs to the captured top-level window. PrintWindow renders only that window, so an
///     element of another window (a detached popup) is never drawn over it.
/// </param>
public sealed record HighlightTarget(string ElementId, Rectangle ScreenBounds, bool InCapturedWindow);

/// <summary>
///     Where a highlighted element landed in the written image.
/// </summary>
/// <param name="ElementId">The AutomationId the caller named.</param>
/// <param name="Visible">Whether any part of the element is inside the image, and so was outlined.</param>
/// <param name="Clipped">Whether part of the element lies outside the image; the outline covers only the part inside.</param>
/// <param name="Bounds">The element's bounds in output-image pixels, clipped to the image; null when not visible.</param>
/// <param name="Reason">
///     Why the element is not visible: <see cref="ScreenshotGeometry.EmptyBounds" />,
///     <see cref="ScreenshotGeometry.OtherWindow" /> or <see cref="ScreenshotGeometry.OutsideCapture" />; null when
///     visible.
/// </param>
public sealed record HighlightPlacement(string ElementId, bool Visible, bool Clipped, Rectangle? Bounds, string? Reason);

/// <summary>
///     Coordinate arithmetic for <c>screenshot</c>, free of UI Automation and GDI so it is unit-testable on any host.
/// </summary>
/// <remarks>
///     Four pixel spaces meet here. UIA bounds and <c>GetWindowRect</c> are physical screen pixels (the UI driver is
///     per-monitor DPI aware). Window-relative physical pixels subtract the window's top-left corner. Surface pixels
///     are the window's PrintWindow backing surface, which equals physical pixels for a per-monitor-aware window and
///     is smaller by <c>windowDpi / monitorDpi</c> otherwise. Output pixels are the cropped surface after any
///     <c>--max-width</c> downscale. Device-independent pixels (DIPs, 1/96 inch) are converted with the monitor's
///     effective DPI, which is what a DIP of the app occupies on screen regardless of the app's own DPI awareness.
/// </remarks>
public static class ScreenshotGeometry
{
    /// <summary>Outline stroke width in output-image pixels, independent of DPI and downscaling.</summary>
    public const int OutlineThickness = 2;

    /// <summary>The element's bounding rectangle is empty: it is offscreen, collapsed, or not laid out.</summary>
    public const string EmptyBounds = "empty-bounds";

    /// <summary>The element belongs to a different top-level window from the one captured.</summary>
    public const string OtherWindow = "other-window";

    /// <summary>The element lies entirely outside the captured region.</summary>
    public const string OutsideCapture = "outside-capture";

    /// <summary>
    ///     Slack, in output pixels, before an edge counts as reaching into the next pixel, so floating-point noise on an
    ///     edge that maps exactly onto a pixel boundary does not widen the highlight.
    /// </summary>
    private const double EdgeTolerance = 1e-6;

    /// <summary>
    ///     Converts a DIP padding to physical pixels, rounding half away from zero.
    /// </summary>
    /// <param name="padDips">Padding in device-independent pixels; zero or more.</param>
    /// <param name="physicalPixelsPerDip">Monitor effective DPI divided by 96 (1.5 at 150% scaling).</param>
    public static int PadPixels(int padDips, double physicalPixelsPerDip)
    {
        return (int)Math.Round(padDips * physicalPixelsPerDip, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    ///     Grows a window-relative physical-pixel crop by <paramref name="padPixels" /> on each side and clamps it to the
    ///     window. Returns an empty rectangle when the padded crop does not intersect the window.
    /// </summary>
    public static Rectangle PadCrop(Rectangle crop, int padPixels, Size window)
    {
        Rectangle padded = Rectangle.Inflate(crop, padPixels, padPixels);
        return Rectangle.Intersect(padded, new Rectangle(Point.Empty, window));
    }

    /// <summary>
    ///     Converts a window-relative physical-pixel crop to surface pixels and clamps it to the surface. Returns null
    ///     when nothing of the crop is inside the surface.
    /// </summary>
    /// <param name="physicalCrop">Window-relative physical pixels.</param>
    /// <param name="surfaceScale">Surface pixels per physical pixel (1.0 for a per-monitor-aware window).</param>
    /// <param name="surface">The backing surface size in surface pixels.</param>
    public static Rectangle? SurfaceCrop(Rectangle physicalCrop, double surfaceScale, Size surface)
    {
        Rectangle crop = new((int)Math.Round(physicalCrop.X * surfaceScale),
            (int)Math.Round(physicalCrop.Y * surfaceScale), (int)Math.Round(physicalCrop.Width * surfaceScale),
            (int)Math.Round(physicalCrop.Height * surfaceScale));
        Rectangle clamped = Rectangle.Intersect(crop, new Rectangle(Point.Empty, surface));
        return clamped.Width > 0 && clamped.Height > 0 ? clamped : null;
    }

    /// <summary>
    ///     The output size for a capture of <paramref name="source" /> pixels: unchanged when it is no wider than
    ///     <paramref name="maxWidth" /> or the limit is zero, otherwise exactly <paramref name="maxWidth" /> wide with the
    ///     height scaled in proportion (at least one pixel). Never larger than the source.
    /// </summary>
    public static Size OutputSize(Size source, int maxWidth)
    {
        if (maxWidth <= 0 || source.Width <= maxWidth)
        {
            return source;
        }

        int height = (int)Math.Round(source.Height * (maxWidth / (double)source.Width),
            MidpointRounding.AwayFromZero);
        return new Size(maxWidth, Math.Clamp(height, 1, source.Height));
    }

    /// <summary>
    ///     Output pixels per source pixel horizontally: 1.0 when not downscaled, below 1.0 otherwise.
    /// </summary>
    public static double ScaleFactor(Size source, Size output)
    {
        return source.Width <= 0 ? 1.0 : output.Width / (double)source.Width;
    }

    /// <summary>
    ///     Maps an element's screen bounds into the output image: screen to window-relative physical pixels, to surface
    ///     pixels, relative to the captured region, then through the downscale, and clips the result to the image.
    /// </summary>
    /// <param name="target">The element and its screen bounds.</param>
    /// <param name="windowOrigin">The captured window's top-left corner in physical screen pixels.</param>
    /// <param name="surfaceScale">Surface pixels per physical pixel.</param>
    /// <param name="source">The captured region in surface pixels.</param>
    /// <param name="output">The output image size.</param>
    public static HighlightPlacement PlaceHighlight(HighlightTarget target, Point windowOrigin, double surfaceScale,
        Rectangle source, Size output)
    {
        Rectangle screen = target.ScreenBounds;
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return new HighlightPlacement(target.ElementId, false, false, null, EmptyBounds);
        }

        if (!target.InCapturedWindow)
        {
            return new HighlightPlacement(target.ElementId, false, false, null, OtherWindow);
        }

        double scaleX = source.Width <= 0 ? 1.0 : output.Width / (double)source.Width;
        double scaleY = source.Height <= 0 ? 1.0 : output.Height / (double)source.Height;

        // Near edges round down and far edges round up, so the rectangle covers every output pixel the element
        // touches: a sub-pixel element after downscaling still occupies the pixel it falls in, including the last
        // pixel of the image.
        int left = (int)Math.Floor(ToOutput(screen.Left, windowOrigin.X, source.X, surfaceScale, scaleX) + EdgeTolerance);
        int right = (int)Math.Ceiling(ToOutput(screen.Right, windowOrigin.X, source.X, surfaceScale, scaleX) -
                                      EdgeTolerance);
        int top = (int)Math.Floor(ToOutput(screen.Top, windowOrigin.Y, source.Y, surfaceScale, scaleY) + EdgeTolerance);
        int bottom = (int)Math.Ceiling(ToOutput(screen.Bottom, windowOrigin.Y, source.Y, surfaceScale, scaleY) -
                                       EdgeTolerance);
        right = Math.Max(right, left + 1);
        bottom = Math.Max(bottom, top + 1);

        Rectangle unclipped = Rectangle.FromLTRB(left, top, right, bottom);
        Rectangle clipped = Rectangle.Intersect(unclipped, new Rectangle(Point.Empty, output));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return new HighlightPlacement(target.ElementId, false, false, null, OutsideCapture);
        }

        return new HighlightPlacement(target.ElementId, true, clipped != unclipped, clipped, null);
    }

    /// <summary>
    ///     The rectangle whose inner <see cref="OutlineThickness" />-pixel ring is the outline for
    ///     <paramref name="bounds" />. The ring sits just outside the element so it does not cover the element's own
    ///     pixels. Where the image edge leaves no room, that side falls on the element's outermost pixels instead.
    /// </summary>
    /// <param name="bounds">Element bounds in output-image pixels, already clipped to the image.</param>
    /// <param name="image">The output image size.</param>
    public static Rectangle OutlineRect(Rectangle bounds, Size image)
    {
        Rectangle grown = Rectangle.Inflate(bounds, OutlineThickness, OutlineThickness);
        return Rectangle.Intersect(grown, new Rectangle(Point.Empty, image));
    }

    private static double ToOutput(int screenEdge, int windowEdge, int sourceEdge, double surfaceScale,
        double outputScale)
    {
        double surface = (screenEdge - windowEdge) * surfaceScale - sourceEdge;
        return surface * outputScale;
    }
}