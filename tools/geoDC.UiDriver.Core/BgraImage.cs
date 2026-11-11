using System.Drawing;

namespace geoDC.UiDriver;

/// <summary>
///     A top-down, 32-bit BGRA pixel buffer (row stride <c>Width * 4</c>) with the crop, downscale and outline
///     operations <c>screenshot</c> applies between capture and PNG encoding. Plain managed code, so it runs and
///     tests on any host and never touches the captured application.
/// </summary>
public sealed class BgraImage
{
    private const int BytesPerPixel = 4;

    /// <summary>
    ///     Wraps an existing buffer without copying it.
    /// </summary>
    /// <exception cref="ArgumentException">The buffer length is not <c>width * height * 4</c>.</exception>
    public BgraImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height * BytesPerPixel)
        {
            throw new ArgumentException(
                $"Expected {width * height * BytesPerPixel} bytes for {width}x{height} BGRA but got {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels; always positive.</summary>
    public int Width { get; }

    /// <summary>Height in pixels; always positive.</summary>
    public int Height { get; }

    /// <summary><see cref="Width" /> and <see cref="Height" /> in pixels.</summary>
    public Size Size => new(Width, Height);

    /// <summary>Bytes per row of <see cref="Pixels" />: <c>Width * 4</c>, with no row padding.</summary>
    public int Stride => Width * BytesPerPixel;

    /// <summary>Pixel bytes in B, G, R, A order, row by row from the top.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    ///     Copies out <paramref name="region" />, which must lie inside the image and be non-empty.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The region is empty or extends past the image.</exception>
    public BgraImage Crop(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0 || region.Right > Width ||
            region.Bottom > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region),
                $"Crop {region} is empty or outside the {Width}x{Height} image.");
        }

        int rowBytes = region.Width * BytesPerPixel;
        byte[] cropped = new byte[rowBytes * region.Height];
        for (int row = 0; row < region.Height; row++)
        {
            Buffer.BlockCopy(Pixels, (region.Y + row) * Stride + region.X * BytesPerPixel, cropped, row * rowBytes,
                rowBytes);
        }

        return new BgraImage(region.Width, region.Height, cropped);
    }

    /// <summary>
    ///     Resamples to <paramref name="width" /> x <paramref name="height" /> by area averaging: each output pixel is
    ///     the coverage-weighted mean of the source pixels under it, so thin lines and text fade rather than drop out.
    ///     Returns this instance when the size is unchanged.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The target is empty or larger than the source on either axis.</exception>
    public BgraImage Downscale(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > Width || height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Downscale target {width}x{height} must be non-empty and no larger than {Width}x{Height}.");
        }

        if (width == Width && height == Height)
        {
            return this;
        }

        Contribution[] columns = Contributions(Width, width);
        Contribution[] rows = Contributions(Height, height);

        // Horizontal pass into a float buffer (source rows x output columns), then a vertical pass to bytes.
        float[] horizontal = new float[Height * width * BytesPerPixel];
        for (int y = 0; y < Height; y++)
        {
            int sourceRow = y * Stride;
            int targetRow = y * width * BytesPerPixel;
            for (int x = 0; x < width; x++)
            {
                Contribution column = columns[x];
                float b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < column.Weights.Length; k++)
                {
                    int source = sourceRow + (column.First + k) * BytesPerPixel;
                    float weight = column.Weights[k];
                    b += Pixels[source] * weight;
                    g += Pixels[source + 1] * weight;
                    r += Pixels[source + 2] * weight;
                    a += Pixels[source + 3] * weight;
                }

                int target = targetRow + x * BytesPerPixel;
                horizontal[target] = b;
                horizontal[target + 1] = g;
                horizontal[target + 2] = r;
                horizontal[target + 3] = a;
            }
        }

        int outputStride = width * BytesPerPixel;
        byte[] output = new byte[outputStride * height];
        for (int y = 0; y < height; y++)
        {
            Contribution row = rows[y];
            for (int x = 0; x < outputStride; x++)
            {
                float sum = 0;
                for (int k = 0; k < row.Weights.Length; k++)
                {
                    sum += horizontal[(row.First + k) * outputStride + x] * row.Weights[k];
                }

                output[y * outputStride + x] = (byte)Math.Clamp((int)MathF.Round(sum), 0, 255);
            }
        }

        return new BgraImage(width, height, output);
    }

    /// <summary>
    ///     Paints the inner ring of <paramref name="outer" />, <paramref name="thickness" /> pixels wide, opaque in
    ///     <paramref name="color" />, clipped to the image. A rectangle thinner than two rings is filled.
    /// </summary>
    public void DrawRing(Rectangle outer, int thickness, Color color)
    {
        Rectangle clipped = Rectangle.Intersect(outer, new Rectangle(0, 0, Width, Height));
        if (clipped.Width <= 0 || clipped.Height <= 0 || thickness <= 0)
        {
            return;
        }

        Rectangle inner = Rectangle.Inflate(outer, -thickness, -thickness);
        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (int x = clipped.Left; x < clipped.Right; x++)
            {
                if (inner.Width > 0 && inner.Height > 0 && inner.Contains(x, y))
                {
                    continue;
                }

                int offset = y * Stride + x * BytesPerPixel;
                Pixels[offset] = color.B;
                Pixels[offset + 1] = color.G;
                Pixels[offset + 2] = color.R;
                Pixels[offset + 3] = 0xFF;
            }
        }
    }

    /// <summary>
    ///     For each output index, the source indices it covers and their weights (fraction of the output pixel's span
    ///     each source pixel covers; the weights sum to one).
    /// </summary>
    private static Contribution[] Contributions(int sourceLength, int targetLength)
    {
        double ratio = sourceLength / (double)targetLength;
        Contribution[] contributions = new Contribution[targetLength];
        for (int target = 0; target < targetLength; target++)
        {
            double start = target * ratio;
            double end = Math.Min((target + 1) * ratio, sourceLength);
            int first = (int)Math.Floor(start);
            int last = Math.Min((int)Math.Ceiling(end), sourceLength) - 1;
            float[] weights = new float[last - first + 1];
            for (int source = first; source <= last; source++)
            {
                double covered = Math.Min(end, source + 1) - Math.Max(start, source);
                weights[source - first] = (float)(Math.Max(covered, 0) / ratio);
            }

            contributions[target] = new Contribution(first, weights);
        }

        return contributions;
    }

    private readonly record struct Contribution(int First, float[] Weights);
}