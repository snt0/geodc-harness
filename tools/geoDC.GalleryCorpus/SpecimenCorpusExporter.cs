using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using geoDC.App.UI.Markup;
using geoDC.App.UI.Sample.Gallery;
using geoDC.App.UI.Theming;

namespace geoDC.GalleryCorpus;

/// <summary>
///     Writes the screenshot corpus: <c>&lt;OutDir&gt;/&lt;theme&gt;/&lt;Id&gt;.png</c> per specimen and theme, and
///     <c>manifest.json</c> listing every specimen, including those left for the UI driver. Two exports from the same
///     commit are byte-identical.
/// </summary>
/// <param name="themes">Switches the theme between passes; the original theme is restored after.</param>
/// <param name="renderer">Renders each page.</param>
public sealed class SpecimenCorpusExporter(IThemeService themes, SpecimenPageRenderer renderer)
{
    /// <summary>The manifest's file name in the output directory.</summary>
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Renders <paramref name="registrations" /> in each of <paramref name="variants" /> into <paramref name="outputDirectory" />.</summary>
    /// <param name="outputDirectory">The corpus folder; its manifest is replaced.</param>
    /// <param name="registrations">The pages to render, in gallery order.</param>
    /// <param name="variants">The themes to capture, in order.</param>
    /// <param name="commit">The commit the gallery was built from, recorded in the manifest.</param>
    /// <param name="include">
    ///     Picks the figures to write; <see langword="null" /> writes them all and first clears the theme folders'
    ///     PNGs. With a filter, other existing images are kept and the manifest lists only the picked figures.
    /// </param>
    public SpecimenCorpusExport Export(string outputDirectory, IReadOnlyList<GallerySpecimenRegistration> registrations,
        IReadOnlyList<ThemeVariant> variants, string? commit, Func<SpecimenCapture, bool>? include = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(variants);

        Directory.CreateDirectory(outputDirectory);
        List<EntryBuilder> entries = [];
        Dictionary<string, EntryBuilder> entriesById = new(StringComparer.Ordinal);
        List<string> failures = [];
        ThemeVariant original = themes.Current;
        if (include is null)
        {
            foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light }.Union(variants))
            {
                ClearImages(Path.Combine(outputDirectory, ThemeName(theme)));
            }
        }

        try
        {
            foreach (ThemeVariant variant in variants)
            {
                string theme = ThemeName(variant);
                string themeDirectory = Path.Combine(outputDirectory, theme);
                Directory.CreateDirectory(themeDirectory);

                themes.Set(variant);
                HashSet<string> seenInTheme = new(StringComparer.Ordinal);

                foreach (GallerySpecimenRegistration registration in registrations)
                {
                    SpecimenPageRender render = renderer.Render(registration);
                    foreach (SpecimenCapture capture in render.Captures.Where(capture => include?.Invoke(capture) != false))
                    {
                        if (!seenInTheme.Add(capture.Id))
                        {
                            failures.Add($"{theme}/{capture.Id}: the id is used twice; images are named after it");
                            continue;
                        }

                        if (!entriesById.TryGetValue(capture.Id, out EntryBuilder? entry))
                        {
                            entry = new EntryBuilder(capture, render);
                            entriesById.Add(capture.Id, entry);
                            entries.Add(entry);
                        }

                        if (capture.Error is not null)
                        {
                            failures.Add($"{theme}/{capture.Id}: {capture.Error}");
                        }
                        else if (capture.Image is { } image)
                        {
                            entry.Captures.Add(Write(outputDirectory, theme, capture.Id, image));
                        }
                    }
                }
            }
        }
        finally
        {
            themes.Set(original);
        }

        SpecimenCorpusManifest manifest = new(SpecimenCorpusManifest.CurrentSchemaVersion, commit,
            renderer.Options.Dpi, renderer.Options.Width, variants.Select(ThemeName).ToArray(),
            entries.Select(entry => entry.Build()).ToArray());
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName),
            JsonSerializer.Serialize(manifest, ManifestJson) + "\n");
        return new SpecimenCorpusExport(manifest, failures);
    }

    /// <summary>Returns the folder and manifest name of <paramref name="variant" />: <c>dark</c> or <c>light</c>.</summary>
    public static string ThemeName(ThemeVariant variant)
    {
        return variant.ToString().ToLowerInvariant();
    }

    private static void ClearImages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string stale in Directory.EnumerateFiles(directory, "*.png"))
        {
            File.Delete(stale);
        }
    }

    private static SpecimenCorpusManifestCapture Write(string outputDirectory, string theme, string id,
        BitmapSource image)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string file = $"{theme}/{id}.png";
        using (FileStream stream = File.Create(Path.Combine(outputDirectory, theme, $"{id}.png")))
        {
            encoder.Save(stream);
        }

        return new SpecimenCorpusManifestCapture(theme, file, image.PixelWidth, image.PixelHeight, "offscreen",
            HashPixels(image));
    }

    /// <summary>Returns the lowercase hex SHA-256 of <paramref name="image" />'s premultiplied BGRA pixels.</summary>
    public static string HashPixels(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        BitmapSource pixelsSource = image.Format == PixelFormats.Pbgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        int stride = pixelsSource.PixelWidth * 4;
        byte[] pixels = new byte[stride * pixelsSource.PixelHeight];
        pixelsSource.CopyPixels(pixels, stride, 0);
        return Convert.ToHexStringLower(SHA256.HashData(pixels));
    }

    private sealed class EntryBuilder(SpecimenCapture first, SpecimenPageRender page)
    {
        public List<SpecimenCorpusManifestCapture> Captures { get; } = [];

        public SpecimenCorpusManifestEntry Build()
        {
            GallerySpecimenRegistration registration = page.Registration;
            return new SpecimenCorpusManifestEntry(first.Id, first.ParentId is null ? "specimen" : "detail",
                first.ParentId, registration.Control.Name, registration.Group.ToString(),
                GalleryGroupInfo.For(registration.Group).NavItemId, registration.PageType.Name, page.Title,
                first.Caption, first.RequiresLiveWindow, Captures.ToArray());
        }
    }
}