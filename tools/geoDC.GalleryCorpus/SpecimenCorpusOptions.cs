namespace geoDC.GalleryCorpus;

/// <summary>How the screenshot corpus lays out and renders specimen pages.</summary>
/// <param name="Width">The width, in device-independent pixels, every page is laid out at.</param>
/// <param name="Dpi">The resolution captures are rendered at; 96 is one pixel per device-independent pixel.</param>
public sealed record SpecimenCorpusOptions(double Width = SpecimenCorpusOptions.GallerySectionWidth, double Dpi = 96d)
{
    /// <summary>
    ///     The content width of a gallery section in the default 1200-wide window, so offscreen captures match the
    ///     UI driver's: less the window border, nav column, scroll bar, page margins and section border and padding.
    /// </summary>
    public const double GallerySectionWidth = 1046d;

    /// <summary>Gets the number of rendered pixels per device-independent pixel.</summary>
    public double Scale => Dpi / 96d;
}