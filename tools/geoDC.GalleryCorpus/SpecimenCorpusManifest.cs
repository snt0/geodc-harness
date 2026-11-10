using geoDC.App.UI.Sample.Gallery;

namespace geoDC.GalleryCorpus;

/// <summary>
///     The index of a screenshot corpus, written as <c>manifest.json</c> beside the <c>&lt;theme&gt;/&lt;Id&gt;.png</c>
///     files: every specimen in gallery order, including those only the UI driver can capture.
/// </summary>
/// <param name="SchemaVersion">The manifest format version; raised when a field changes meaning or is removed.</param>
/// <param name="Commit">The commit the gallery was built from, or <see langword="null" /> when the build has none.</param>
/// <param name="Dpi">The resolution offscreen captures were rendered at.</param>
/// <param name="Width">The width, in device-independent pixels, pages were laid out at.</param>
/// <param name="Themes">The themes captured, in capture order (<c>dark</c>, <c>light</c>).</param>
/// <param name="Entries">One entry per specimen or detail, in gallery order.</param>
public sealed record SpecimenCorpusManifest(
    int SchemaVersion,
    string? Commit,
    double Dpi,
    double Width,
    IReadOnlyList<string> Themes,
    IReadOnlyList<SpecimenCorpusManifestEntry> Entries)
{
    /// <summary>The <see cref="SchemaVersion" /> this build writes.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One specimen, or one detail inside a specimen, in a <see cref="SpecimenCorpusManifest" />.</summary>
/// <param name="Id">The specimen's id, or the detail's AutomationId; images are named after it.</param>
/// <param name="Kind"><c>specimen</c> for a whole specimen, <c>detail</c> for an element inside one.</param>
/// <param name="Parent">For a detail, the id of the specimen that holds it.</param>
/// <param name="Control">The toolkit type the specimen's page is registered under.</param>
/// <param name="Group">The <see cref="GalleryGroup" /> the page is filed under.</param>
/// <param name="NavItemId">The AutomationId of the group's gallery nav item, which the UI driver clicks to show it.</param>
/// <param name="Page">The specimen page's type name.</param>
/// <param name="PageTitle">The specimen page's title.</param>
/// <param name="Caption">The specimen's caption.</param>
/// <param name="RequiresLiveWindow">Whether only the UI driver can capture it, from the live gallery window.</param>
/// <param name="Captures">One capture per theme that produced an image; empty until the UI driver fills a live one.</param>
public sealed record SpecimenCorpusManifestEntry(
    string Id,
    string Kind,
    string? Parent,
    string Control,
    string Group,
    string NavItemId,
    string Page,
    string PageTitle,
    string Caption,
    bool RequiresLiveWindow,
    IReadOnlyList<SpecimenCorpusManifestCapture> Captures);

/// <summary>One image of a <see cref="SpecimenCorpusManifestEntry" /> in one theme.</summary>
/// <param name="Theme"><c>dark</c> or <c>light</c>.</param>
/// <param name="File">The image's path relative to the manifest, with forward slashes.</param>
/// <param name="PixelWidth">The image's width in pixels.</param>
/// <param name="PixelHeight">The image's height in pixels.</param>
/// <param name="Source"><c>offscreen</c> when rendered by the corpus tool, <c>ui-driver</c> when captured from the live window.</param>
/// <param name="PixelsSha256">
///     The SHA-256 of the image's BGRA pixels, for offscreen captures; two runs on the same commit match. Live-window
///     captures leave it <see langword="null" />.
/// </param>
public sealed record SpecimenCorpusManifestCapture(
    string Theme,
    string File,
    int PixelWidth,
    int PixelHeight,
    string Source,
    string? PixelsSha256);