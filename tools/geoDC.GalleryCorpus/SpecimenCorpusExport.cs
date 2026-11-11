namespace geoDC.GalleryCorpus;

/// <summary>The outcome of writing a screenshot corpus.</summary>
/// <param name="Manifest">The manifest that was written.</param>
/// <param name="Failures">One line per figure that could not be rendered, as <c>&lt;theme&gt;/&lt;Id&gt;: reason</c>.</param>
public sealed record SpecimenCorpusExport(SpecimenCorpusManifest Manifest, IReadOnlyList<string> Failures);