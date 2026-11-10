using geoDC.App.UI.Sample.Gallery;

namespace geoDC.GalleryCorpus;

/// <summary>The figures cut from one specimen page rendered offscreen, in page order.</summary>
/// <param name="Registration">The page that was rendered.</param>
/// <param name="Title">The page's title.</param>
/// <param name="WasLoaded">Whether the page received <c>Loaded</c> before it was rendered.</param>
/// <param name="Captures">One capture per specimen, each followed by the details inside it.</param>
public sealed record SpecimenPageRender(
    GallerySpecimenRegistration Registration,
    string Title,
    bool WasLoaded,
    IReadOnlyList<SpecimenCapture> Captures);