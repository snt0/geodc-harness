using System.Drawing;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class BgraImageTests
{
    private static BgraImage Filled(int width, int height, byte b, byte g, byte r)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0xFF;
        }

        return new BgraImage(width, height, pixels);
    }

    private static void SetPixel(BgraImage image, int x, int y, byte b, byte g, byte r)
    {
        int offset = y * image.Stride + x * 4;
        image.Pixels[offset] = b;
        image.Pixels[offset + 1] = g;
        image.Pixels[offset + 2] = r;
        image.Pixels[offset + 3] = 0xFF;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(BgraImage image, int x, int y)
    {
        int offset = y * image.Stride + x * 4;
        return (image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2], image.Pixels[offset + 3]);
    }

    [Test]
    public void Constructor_RejectsMismatchedBufferLength()
    {
        Assert.Throws<ArgumentException>(() => _ = new BgraImage(2, 2, new byte[15]));
    }

    [Test]
    public void Crop_CopiesTheRegionRowByRow()
    {
        BgraImage image = Filled(4, 3, 0, 0, 0);
        SetPixel(image, 1, 1, 10, 20, 30);
        SetPixel(image, 2, 2, 40, 50, 60);

        BgraImage cropped = image.Crop(new Rectangle(1, 1, 2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(cropped.Size, Is.EqualTo(new Size(2, 2)));
            Assert.That(Pixel(cropped, 0, 0), Is.EqualTo(((byte)10, (byte)20, (byte)30, (byte)0xFF)));
            Assert.That(Pixel(cropped, 1, 1), Is.EqualTo(((byte)40, (byte)50, (byte)60, (byte)0xFF)));
            Assert.That(Pixel(cropped, 1, 0), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)0xFF)));
        });
    }

    [Test]
    public void Crop_OutsideTheImage_Throws()
    {
        BgraImage image = Filled(4, 4, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(new Rectangle(2, 2, 3, 1)));
    }

    [Test]
    public void Downscale_ToSameSize_ReturnsTheSameInstance()
    {
        BgraImage image = Filled(8, 4, 1, 2, 3);

        Assert.That(image.Downscale(8, 4), Is.SameAs(image));
    }

    [Test]
    public void Downscale_Larger_Throws()
    {
        BgraImage image = Filled(8, 4, 1, 2, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Downscale(9, 4));
    }

    [Test]
    public void Downscale_ByHalf_AveragesEachTwoByTwoBlock()
    {
        // A one-pixel checkerboard of black and white averages to mid-grey rather than aliasing to either.
        BgraImage image = Filled(4, 4, 0, 0, 0);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                if ((x + y) % 2 == 0)
                {
                    SetPixel(image, x, y, 255, 255, 255);
                }
            }
        }

        BgraImage scaled = image.Downscale(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(scaled.Size, Is.EqualTo(new Size(2, 2)));
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Assert.That(Pixel(scaled, x, y), Is.EqualTo(((byte)128, (byte)128, (byte)128, (byte)255)));
                }
            }
        });
    }

    [Test]
    public void Downscale_ByNonIntegerRatio_PreservesAUniformColour()
    {
        BgraImage image = Filled(7, 5, 12, 200, 99);

        BgraImage scaled = image.Downscale(3, 2);

        Assert.Multiple(() =>
        {
            for (int y = 0; y < scaled.Height; y++)
            {
                for (int x = 0; x < scaled.Width; x++)
                {
                    Assert.That(Pixel(scaled, x, y), Is.EqualTo(((byte)12, (byte)200, (byte)99, (byte)255)));
                }
            }
        });
    }

    [Test]
    public void Downscale_WeightsPartialCoverage()
    {
        // Three source columns into two: output column 0 covers source 0 fully and half of source 1.
        BgraImage image = Filled(3, 1, 0, 0, 0);
        SetPixel(image, 0, 0, 255, 255, 255);

        BgraImage scaled = image.Downscale(2, 1);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(scaled, 0, 0).B, Is.EqualTo(170));
            Assert.That(Pixel(scaled, 1, 0).B, Is.EqualTo(0));
        });
    }

    [Test]
    public void DrawRing_PaintsOnlyTheInnerRingOfTheRectangle()
    {
        BgraImage image = Filled(10, 10, 0, 0, 0);

        image.DrawRing(new Rectangle(2, 2, 6, 6), 2, Color.FromArgb(255, 0, 255));

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(image, 2, 2), Is.EqualTo(((byte)255, (byte)0, (byte)255, (byte)255)));
            Assert.That(Pixel(image, 3, 5), Is.EqualTo(((byte)255, (byte)0, (byte)255, (byte)255)));
            Assert.That(Pixel(image, 7, 7), Is.EqualTo(((byte)255, (byte)0, (byte)255, (byte)255)));
            Assert.That(Pixel(image, 4, 4), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)255)), "interior");
            Assert.That(Pixel(image, 5, 5), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)255)), "interior");
            Assert.That(Pixel(image, 1, 1), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)255)), "outside");
            Assert.That(Pixel(image, 8, 8), Is.EqualTo(((byte)0, (byte)0, (byte)0, (byte)255)), "outside");
        });
    }

    [Test]
    public void DrawRing_ClipsToTheImage()
    {
        BgraImage image = Filled(4, 4, 0, 0, 0);

        Assert.DoesNotThrow(() => image.DrawRing(new Rectangle(-3, -3, 10, 10), 2, Color.Magenta));
    }
}