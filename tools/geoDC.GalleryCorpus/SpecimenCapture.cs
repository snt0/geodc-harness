using System.Windows.Media.Imaging;
using geoDC.App.UI.Sample.Gallery;

namespace geoDC.GalleryCorpus;

/// <summary>One figure cut from a rendered specimen page: a whole <see cref="Specimen" />, or a detail inside one.</summary>
/// <param name="Id">The specimen's <see cref="Specimen.Id" />, or the detail's AutomationId.</param>
/// <param name="ParentId">For a detail, the id of the specimen that holds it; <see langword="null" /> for a specimen.</param>
/// <param name="Caption">The specimen's caption; a detail carries its specimen's.</param>
/// <param name="RequiresLiveWindow">
///     Whether the figure is skipped offscreen because its specimen is <see cref="Specimen.RequiresLiveWindow" />.
/// </param>
/// <param name="Image">The rendered pixels, or <see langword="null" /> when the figure was skipped or failed.</param>
/// <param name="Error">Why the figure could not be rendered, or <see langword="null" />.</param>
public sealed record SpecimenCapture(
    string Id,
    string? ParentId,
    string Caption,
    bool RequiresLiveWindow,
    BitmapSource? Image,
    string? Error);