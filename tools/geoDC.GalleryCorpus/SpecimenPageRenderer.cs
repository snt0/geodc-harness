using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using geoDC.App.UI.Markup;
using geoDC.App.UI.Sample.Gallery;

namespace geoDC.GalleryCorpus;

/// <summary>
///     Renders a specimen page offscreen in the current theme and cuts a capture out for each <see cref="Specimen" />.
/// </summary>
/// <remarks>
///     The page sits in an opaque host that supplies what the gallery window would (section background, text
///     foreground and font, ClearType), on a <see cref="Canvas" /> rooted in a hidden <see cref="HwndSource" /> so
///     <c>Loaded</c> runs. The host is rendered directly rather than through a <see cref="VisualBrush" />, which
///     would lose ClearType.
/// </remarks>
public sealed partial class SpecimenPageRenderer(SpecimenCorpusOptions options)
{
    // WS_POPUP without WS_VISIBLE.
    private const int HiddenPopupStyle = unchecked((int)0x80000000);

    /// <summary>Gets the options the renderer lays out and renders with.</summary>
    public SpecimenCorpusOptions Options { get; } = options;

    /// <summary>
    ///     Creates a fresh instance of the registered page, renders it, and returns a capture for every specimen in it,
    ///     plus one for every detail inside a specimen that carries an AutomationId of its own ending in <c>Panel</c>.
    /// </summary>
    /// <param name="registration">The page to render.</param>
    public SpecimenPageRender Render(GallerySpecimenRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        SpecimenPage page = registration.CreatePage();
        Border host = CreateHost(page);
        Canvas root = new() { Children = { host } };

        HwndSourceParameters parameters = new("geoDC specimen corpus")
        {
            WindowStyle = HiddenPopupStyle,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1
        };

        using HwndSource source = new(parameters);
        source.RootVisual = root;
        Settle(host);
        bool wasLoaded = page.IsLoaded;

        List<SpecimenCapture> captures = [];
        BitmapSource? frame = null;
        foreach (Specimen specimen in Descendants(page).OfType<Specimen>())
        {
            string id = specimen.Id ?? string.Empty;
            string caption = specimen.Caption ?? string.Empty;
            if (specimen.RequiresLiveWindow)
            {
                captures.Add(new SpecimenCapture(id, null, caption, true, null, null));
                continue;
            }

            frame ??= RenderHost(host);
            captures.Add(Crop(host, frame, specimen, id, null, caption));
            foreach (FrameworkElement detail in Details(specimen))
            {
                captures.Add(Crop(host, frame, detail, AutomationProperties.GetAutomationId(detail), id, caption));
            }
        }

        source.RootVisual = null;
        return new SpecimenPageRender(registration, page.Title, wasLoaded, captures);
    }

    private Border CreateHost(SpecimenPage page)
    {
        Border host = new()
        {
            Width = Options.Width,
            Child = page,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        host.SetResourceReference(Border.BackgroundProperty, ThemeKey(ThemeResourceKey.BrushBgSecondary));
        host.SetResourceReference(TextElement.ForegroundProperty, ThemeKey(ThemeResourceKey.BrushTextPrimary));
        host.SetResourceReference(TextElement.FontFamilyProperty, ThemeKey(ThemeResourceKey.FontUi));
        TextOptions.SetTextRenderingMode(host, TextRenderingMode.ClearType);
        return host;
    }

    private static object ThemeKey(ThemeResourceKey resource)
    {
        return new ThemeResourceExtension(resource).ResourceKey;
    }

    // Two idle passes: the first lets Loaded run, the second the layout and bindings that Loaded handlers change.
    private static void Settle(FrameworkElement host)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            host.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            host.UpdateLayout();
        }
    }

    private BitmapSource RenderHost(FrameworkElement host)
    {
        RenderTargetBitmap target = new(
            Math.Max(1, (int)Math.Ceiling(host.ActualWidth * Options.Scale)),
            Math.Max(1, (int)Math.Ceiling(host.ActualHeight * Options.Scale)),
            Options.Dpi, Options.Dpi, PixelFormats.Pbgra32);
        target.Render(host);
        target.Freeze();
        return target;
    }

    private SpecimenCapture Crop(FrameworkElement host, BitmapSource frame, FrameworkElement element, string id,
        string? parentId, string caption)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return new SpecimenCapture(id, parentId, caption, false, null, "not laid out");
        }

        Rect bounds = element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
        int left = Math.Clamp((int)Math.Round(bounds.Left * Options.Scale), 0, frame.PixelWidth);
        int top = Math.Clamp((int)Math.Round(bounds.Top * Options.Scale), 0, frame.PixelHeight);
        int right = Math.Clamp((int)Math.Round(bounds.Right * Options.Scale), 0, frame.PixelWidth);
        int bottom = Math.Clamp((int)Math.Round(bounds.Bottom * Options.Scale), 0, frame.PixelHeight);
        if (right <= left || bottom <= top)
        {
            return new SpecimenCapture(id, parentId, caption, false, null, "outside the rendered page");
        }

        int width = right - left;
        int height = bottom - top;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        frame.CopyPixels(new Int32Rect(left, top, width, height), pixels, stride, 0);
        BitmapSource image = BitmapSource.Create(width, height, Options.Dpi, Options.Dpi, PixelFormats.Pbgra32, null,
            pixels, stride);
        image.Freeze();
        return new SpecimenCapture(id, parentId, caption, false, image, null);
    }

    // Page-declared elements with a *Panel AutomationId, excluding template parts and nested specimens.
    private static IEnumerable<FrameworkElement> Details(Specimen specimen)
    {
        return Descendants(specimen, stopAt: static element => element is Specimen)
            .OfType<FrameworkElement>()
            .Where(element => element.TemplatedParent is null &&
                              DetailIdPattern().IsMatch(AutomationProperties.GetAutomationId(element)));
    }

    // Depth first in child order, so the order is stable across runs.
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root,
        Func<DependencyObject, bool>? stopAt = null)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            if (stopAt?.Invoke(child) == true)
            {
                continue;
            }

            foreach (DependencyObject descendant in Descendants(child, stopAt))
            {
                yield return descendant;
            }
        }
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*Panel$")]
    private static partial Regex DetailIdPattern();
}