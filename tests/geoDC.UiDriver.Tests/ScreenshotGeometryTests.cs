using System.Drawing;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     Screenshot geometry across display scalings. The tests model a 1280x720 DIP window at screen
///     (100, 50) holding an 80x24 DIP element at DIP (200, 100). At scaling <c>s</c>, a per-monitor-aware window
///     reports both in physical pixels multiplied by <c>s</c>.
/// </summary>
[TestFixture]
public sealed class ScreenshotGeometryTests
{
    private static readonly Point WindowOrigin = new(100, 50);

    private static Rectangle Scaled(int x, int y, int width, int height, double scaling)
    {
        return new Rectangle((int)(x * scaling), (int)(y * scaling), (int)(width * scaling), (int)(height * scaling));
    }

    private static Rectangle ElementScreenBounds(double scaling)
    {
        Rectangle relative = Scaled(200, 100, 80, 24, scaling);
        relative.Offset(WindowOrigin);
        return relative;
    }

    [TestCase(10, 1.0, 10)]
    [TestCase(10, 1.5, 15)]
    [TestCase(10, 2.0, 20)]
    [TestCase(2, 1.25, 3)]
    public void PadPixels_ConvertsDipsWithTheMonitorScaling(int dips, double pixelsPerDip, int expected)
    {
        Assert.That(ScreenshotGeometry.PadPixels(dips, pixelsPerDip), Is.EqualTo(expected));
    }

    [Test]
    public void PadCrop_GrowsEachSide()
    {
        Rectangle padded = ScreenshotGeometry.PadCrop(new Rectangle(50, 40, 100, 30), 15, new Size(1920, 1080));

        Assert.That(padded, Is.EqualTo(new Rectangle(35, 25, 130, 60)));
    }

    [Test]
    public void PadCrop_ClampsToTheWindow()
    {
        Rectangle padded = ScreenshotGeometry.PadCrop(new Rectangle(5, 5, 50, 90), 20, new Size(100, 100));

        Assert.That(padded, Is.EqualTo(new Rectangle(0, 0, 75, 100)));
    }

    [Test]
    public void SurfaceCrop_ScalesToSurfacePixelsAndClamps()
    {
        Rectangle? crop = ScreenshotGeometry.SurfaceCrop(new Rectangle(400, 200, 2400, 100), 0.5, new Size(1280, 720));

        Assert.That(crop, Is.EqualTo(new Rectangle(200, 100, 1080, 50)));
    }

    [Test]
    public void SurfaceCrop_OutsideTheSurface_IsNull()
    {
        Assert.That(ScreenshotGeometry.SurfaceCrop(new Rectangle(2000, 0, 10, 10), 1.0, new Size(1280, 720)),
            Is.Null);
    }

    [TestCase(1.0, 1280, 720)]
    [TestCase(1.5, 1600, 900)]
    [TestCase(2.0, 1600, 900)]
    public void OutputSize_FullWindowFitsTheDefaultMaxWidth(double scaling, int expectedWidth, int expectedHeight)
    {
        Size window = new((int)(1280 * scaling), (int)(720 * scaling));

        Size output = ScreenshotGeometry.OutputSize(window, ScreenshotOptions.DefaultMaxWidth);

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(new Size(expectedWidth, expectedHeight)));
            Assert.That(output.Width, Is.LessThanOrEqualTo(ScreenshotOptions.DefaultMaxWidth));
        });
    }

    [Test]
    public void OutputSize_ZeroMaxWidth_KeepsTheSourceSize()
    {
        Assert.That(ScreenshotGeometry.OutputSize(new Size(3840, 2160), 0), Is.EqualTo(new Size(3840, 2160)));
    }

    [Test]
    public void OutputSize_NeverUpscales()
    {
        Assert.That(ScreenshotGeometry.OutputSize(new Size(300, 200), 1600), Is.EqualTo(new Size(300, 200)));
    }

    [Test]
    public void OutputSize_KeepsAtLeastOnePixelOfHeight()
    {
        Assert.That(ScreenshotGeometry.OutputSize(new Size(4000, 1), 100), Is.EqualTo(new Size(100, 1)));
    }

    [Test]
    public void ScaleFactor_IsOutputOverSourceWidth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScreenshotGeometry.ScaleFactor(new Size(2560, 1440), new Size(1600, 900)), Is.EqualTo(0.625));
            Assert.That(ScreenshotGeometry.ScaleFactor(new Size(800, 600), new Size(800, 600)), Is.EqualTo(1.0));
        });
    }

    [TestCase(1.0)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void PlaceHighlight_FullWindowWithoutDownscale_MatchesWindowRelativePhysicalBounds(double scaling)
    {
        Size surface = new((int)(1280 * scaling), (int)(720 * scaling));
        HighlightTarget target = new("SaveButton", ElementScreenBounds(scaling), true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0,
            new Rectangle(Point.Empty, surface), surface);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Visible, Is.True);
            Assert.That(placement.Clipped, Is.False);
            Assert.That(placement.Reason, Is.Null);
            Assert.That(placement.Bounds, Is.EqualTo(Scaled(200, 100, 80, 24, scaling)));
        });
    }

    [TestCase(1.0, 200, 100, 80, 24)]
    [TestCase(1.5, 250, 125, 100, 30)]
    [TestCase(2.0, 250, 125, 100, 30)]
    public void PlaceHighlight_FullWindowAtDefaultMaxWidth_FollowsTheDownscale(double scaling, int x, int y,
        int width, int height)
    {
        Size surface = new((int)(1280 * scaling), (int)(720 * scaling));
        Size output = ScreenshotGeometry.OutputSize(surface, ScreenshotOptions.DefaultMaxWidth);
        HighlightTarget target = new("SaveButton", ElementScreenBounds(scaling), true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0,
            new Rectangle(Point.Empty, surface), output);

        Assert.That(placement.Bounds, Is.EqualTo(new Rectangle(x, y, width, height)));
    }

    [Test]
    public void PlaceHighlight_ThinElementOnTheFarEdgesOfADownscale_StaysInTheLastPixel()
    {
        // 3840x2160 downscaled to 1600x900: a one-pixel column at x=3839 and a one-pixel row at y=2159 map both of
        // their edges onto the image boundary, and must still land in the last column and row rather than outside.
        Size surface = new(3840, 2160);
        Size output = ScreenshotGeometry.OutputSize(surface, ScreenshotOptions.DefaultMaxWidth);
        HighlightTarget column = new("Column", new Rectangle(WindowOrigin.X + 3839, WindowOrigin.Y + 10, 1, 100), true);
        HighlightTarget row = new("Row", new Rectangle(WindowOrigin.X + 10, WindowOrigin.Y + 2159, 100, 1), true);

        HighlightPlacement columnPlacement = ScreenshotGeometry.PlaceHighlight(column, WindowOrigin, 1.0,
            new Rectangle(Point.Empty, surface), output);
        HighlightPlacement rowPlacement = ScreenshotGeometry.PlaceHighlight(row, WindowOrigin, 1.0,
            new Rectangle(Point.Empty, surface), output);

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(new Size(1600, 900)));
            Assert.That(columnPlacement.Visible, Is.True);
            Assert.That(columnPlacement.Bounds, Is.EqualTo(new Rectangle(1599, 4, 1, 42)));
            Assert.That(rowPlacement.Visible, Is.True);
            Assert.That(rowPlacement.Bounds, Is.EqualTo(new Rectangle(4, 899, 42, 1)));
        });
    }

    [Test]
    public void PlaceHighlight_WindowNotPerMonitorAware_MapsThroughTheSurfaceScale()
    {
        // A DPI-unaware window on a 200% monitor renders a 1280x720 surface that DWM stretches to 2560x1440.
        HighlightTarget target = new("SaveButton", ElementScreenBounds(2.0), true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 0.5,
            new Rectangle(0, 0, 1280, 720), new Size(1280, 720));

        Assert.That(placement.Bounds, Is.EqualTo(new Rectangle(200, 100, 80, 24)));
    }

    [Test]
    public void PlaceHighlight_InsidePaddedParentCrop_IsRelativeToTheCrop()
    {
        // At 150%: a parent at DIP (180, 80, 200, 100) padded by 10 DIP (15 px) on each side.
        Size window = new(1920, 1080);
        int padPixels = ScreenshotGeometry.PadPixels(10, 1.5);
        Rectangle physicalCrop = ScreenshotGeometry.PadCrop(Scaled(180, 80, 200, 100, 1.5), padPixels, window);
        Rectangle source = ScreenshotGeometry.SurfaceCrop(physicalCrop, 1.0, window) ?? Rectangle.Empty;
        Size output = ScreenshotGeometry.OutputSize(source.Size, ScreenshotOptions.DefaultMaxWidth);
        HighlightTarget target = new("SaveButton", ElementScreenBounds(1.5), true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0, source, output);

        Assert.Multiple(() =>
        {
            Assert.That(source, Is.EqualTo(new Rectangle(255, 105, 330, 180)));
            Assert.That(output, Is.EqualTo(source.Size));
            Assert.That(placement.Bounds, Is.EqualTo(new Rectangle(45, 45, 120, 36)));
            Assert.That(placement.Clipped, Is.False);
        });
    }

    [Test]
    public void PlaceHighlight_PartlyOutsideTheCrop_IsClippedToTheImage()
    {
        // Crop starts 40 px into the element horizontally.
        Rectangle source = new(240, 50, 200, 200);
        HighlightTarget target = new("SaveButton", ElementScreenBounds(1.0), true);

        HighlightPlacement placement =
            ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0, source, source.Size);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Visible, Is.True);
            Assert.That(placement.Clipped, Is.True);
            Assert.That(placement.Bounds, Is.EqualTo(new Rectangle(0, 50, 40, 24)));
        });
    }

    [Test]
    public void PlaceHighlight_EntirelyOutsideTheCrop_IsReportedNotVisible()
    {
        Rectangle source = new(600, 400, 200, 200);
        HighlightTarget target = new("SaveButton", ElementScreenBounds(1.0), true);

        HighlightPlacement placement =
            ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0, source, source.Size);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Visible, Is.False);
            Assert.That(placement.Bounds, Is.Null);
            Assert.That(placement.Reason, Is.EqualTo(ScreenshotGeometry.OutsideCapture));
        });
    }

    [Test]
    public void PlaceHighlight_TouchingTheCropEdgeOnly_IsNotVisible()
    {
        // The element's right edge (window-relative x = 280) is the crop's left edge.
        Rectangle source = new(280, 0, 200, 200);
        HighlightTarget target = new("SaveButton", ElementScreenBounds(1.0), true);

        HighlightPlacement placement =
            ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0, source, source.Size);

        Assert.That(placement.Reason, Is.EqualTo(ScreenshotGeometry.OutsideCapture));
    }

    [Test]
    public void PlaceHighlight_EmptyBounds_IsReportedNotVisible()
    {
        HighlightTarget target = new("CollapsedPanel", Rectangle.Empty, true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0,
            new Rectangle(0, 0, 1280, 720), new Size(1280, 720));

        Assert.Multiple(() =>
        {
            Assert.That(placement.Visible, Is.False);
            Assert.That(placement.Reason, Is.EqualTo(ScreenshotGeometry.EmptyBounds));
        });
    }

    [Test]
    public void PlaceHighlight_InAnotherWindow_IsReportedNotVisible()
    {
        HighlightTarget target = new("PopupPanel", ElementScreenBounds(1.0), false);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0,
            new Rectangle(0, 0, 1280, 720), new Size(1280, 720));

        Assert.Multiple(() =>
        {
            Assert.That(placement.Visible, Is.False);
            Assert.That(placement.Reason, Is.EqualTo(ScreenshotGeometry.OtherWindow));
        });
    }

    [Test]
    public void PlaceHighlight_SubPixelAfterDownscale_KeepsOnePixel()
    {
        Rectangle screen = new(WindowOrigin.X + 1000, WindowOrigin.Y + 1000, 2, 2);
        HighlightTarget target = new("Divider", screen, true);

        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(target, WindowOrigin, 1.0,
            new Rectangle(0, 0, 4000, 4000), new Size(400, 400));

        Assert.That(placement.Bounds, Is.EqualTo(new Rectangle(100, 100, 1, 1)));
    }

    [Test]
    public void OutlineRect_SitsOutsideTheElementWhereThereIsRoom()
    {
        Rectangle outline = ScreenshotGeometry.OutlineRect(new Rectangle(250, 125, 100, 30), new Size(1600, 900));

        Assert.That(outline, Is.EqualTo(new Rectangle(248, 123, 104, 34)));
    }

    [Test]
    public void OutlineRect_AtTheImageEdge_FallsOnTheElement()
    {
        // An element that fills the image (an --id crop highlighting itself) is outlined on its own edge pixels.
        Rectangle outline = ScreenshotGeometry.OutlineRect(new Rectangle(0, 0, 120, 36), new Size(120, 36));

        Assert.That(outline, Is.EqualTo(new Rectangle(0, 0, 120, 36)));
    }

    [Test]
    public void Pipeline_At200Percent_DrawsTheOutlineAgainstTheElementEdges()
    {
        // Full-window capture of a per-monitor-aware 200% window: white, with the element painted black.
        Size surface = new(2560, 1440);
        Rectangle elementRelative = Scaled(200, 100, 80, 24, 2.0);
        byte[] pixels = new byte[surface.Width * surface.Height * 4];
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                byte value = elementRelative.Contains(x, y) ? (byte)0 : (byte)255;
                int offset = (y * surface.Width + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        BgraImage image = new(surface.Width, surface.Height, pixels);
        Size output = ScreenshotGeometry.OutputSize(surface, ScreenshotOptions.DefaultMaxWidth);
        BgraImage written = image.Downscale(output.Width, output.Height);
        HighlightPlacement placement = ScreenshotGeometry.PlaceHighlight(
            new HighlightTarget("SaveButton", ElementScreenBounds(2.0), true), WindowOrigin, 1.0,
            new Rectangle(Point.Empty, surface), output);
        Rectangle bounds = placement.Bounds ?? Rectangle.Empty;
        written.DrawRing(ScreenshotGeometry.OutlineRect(bounds, output), ScreenshotGeometry.OutlineThickness,
            Color.FromArgb(255, 0, 255));

        byte Red(int x, int y)
        {
            return written.Pixels[y * written.Stride + x * 4 + 2];
        }

        byte Green(int x, int y)
        {
            return written.Pixels[y * written.Stride + x * 4 + 1];
        }

        Assert.Multiple(() =>
        {
            Assert.That(written.Size, Is.EqualTo(new Size(1600, 900)));
            Assert.That(bounds, Is.EqualTo(new Rectangle(250, 125, 100, 30)));

            // The element's own corner pixels stay black; the two pixels just outside each edge are magenta.
            Assert.That((Red(bounds.Left, bounds.Top), Green(bounds.Left, bounds.Top)), Is.EqualTo(((byte)0, (byte)0)));
            Assert.That((Red(bounds.Right - 1, bounds.Bottom - 1), Green(bounds.Right - 1, bounds.Bottom - 1)),
                Is.EqualTo(((byte)0, (byte)0)));
            Assert.That((Red(bounds.Left - 1, bounds.Top + 10), Green(bounds.Left - 1, bounds.Top + 10)),
                Is.EqualTo(((byte)255, (byte)0)));
            Assert.That((Red(bounds.Left - 2, bounds.Top + 10), Green(bounds.Left - 2, bounds.Top + 10)),
                Is.EqualTo(((byte)255, (byte)0)));
            Assert.That((Red(bounds.Right, bounds.Top + 10), Green(bounds.Right, bounds.Top + 10)),
                Is.EqualTo(((byte)255, (byte)0)));
            Assert.That((Red(bounds.Left + 10, bounds.Bottom + 1), Green(bounds.Left + 10, bounds.Bottom + 1)),
                Is.EqualTo(((byte)255, (byte)0)));

            // Beyond the two-pixel ring the background is untouched.
            Assert.That((Red(bounds.Left - 3, bounds.Top + 10), Green(bounds.Left - 3, bounds.Top + 10)),
                Is.EqualTo(((byte)255, (byte)255)));
        });
    }
}