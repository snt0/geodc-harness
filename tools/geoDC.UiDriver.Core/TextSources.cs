using System.Text;
using geoDC.FindText;

namespace geoDC.UiDriver;

/// <summary>A text the walked elements show, as <c>source</c> looks it up, and the first element that shows it.</summary>
/// <param name="Text">The text: an element's UIA Name, or the value of its ValuePattern.</param>
/// <param name="Role">The control type of the first element that shows it.</param>
/// <param name="AutomationId">That element's AutomationId, or null when it has none.</param>
/// <param name="Path">The AutomationIds of that element's ancestors, from the walk's root down, as <c>find</c> prints them.</param>
/// <param name="Elements">How many walked elements show the text; a button and the text inside it both do.</param>
public sealed record ShownText(string Text, string Role, string? AutomationId, IReadOnlyList<string> Path, int Elements);

/// <summary>A shown text and the strings-file texts it could have come from, closest first.</summary>
/// <param name="Shown">The text and where it is shown.</param>
/// <param name="Candidates">
///     The strings-file texts that match it: none for data or text built in code, and more than one when the source
///     is ambiguous.
/// </param>
public sealed record TextSource(ShownText Shown, IReadOnlyList<TextCandidate> Candidates)
{
    /// <summary>
    ///     The text output for one shown text: a line naming the text and the element that shows it, then each
    ///     candidate with the lines that read it, or a line saying no strings entry matches it.
    /// </summary>
    public string Format()
    {
        StringBuilder text = new();
        text.Append('"').Append(Escape(Shown.Text)).Append("\"  [").Append(Shown.Role).Append(']');
        if (Shown.AutomationId is not null)
        {
            text.Append(" id: ").Append(Shown.AutomationId);
        }

        if (Shown.Elements > 1)
        {
            text.Append("  ×").Append(Shown.Elements);
        }

        text.Append("  path: ").Append(Shown.Path.Count > 0 ? string.Join(" > ", Shown.Path) : "—").Append('\n');
        if (Candidates.Count == 0)
        {
            text.Append("  no strings entry reads as this: it is data, or text built in code\n");
            return text.ToString();
        }

        foreach (TextCandidate candidate in Candidates)
        {
            text.Append("  ").Append(candidate.Describe()).Append('\n');
            if (candidate.Reads.Count == 0)
            {
                text.Append("    read nowhere under src\n");
            }

            foreach (SourceLocation read in candidate.Reads)
            {
                text.Append("    read at ").Append(read.File).Append(':').Append(read.Line).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string Escape(string text)
    {
        return text.Replace("\r", string.Empty).Replace("\n", "\\n");
    }
}

/// <summary>How many shown texts a <c>source</c> run found, and how many of them it traced to the strings files.</summary>
/// <param name="Texts">The distinct texts shown.</param>
/// <param name="Sourced">The texts with at least one candidate.</param>
/// <param name="Ambiguous">The texts with more than one candidate; they are counted in <paramref name="Sourced" /> too.</param>
/// <param name="Unsourced">The texts with no candidate: data, or text built in code.</param>
public sealed record TextSourceSummary(int Texts, int Sourced, int Ambiguous, int Unsourced)
{
    /// <summary>Counts <paramref name="sources" />.</summary>
    public static TextSourceSummary Of(IReadOnlyList<TextSource> sources)
    {
        int sourced = sources.Count(source => source.Candidates.Count > 0);
        return new TextSourceSummary(sources.Count, sourced, sources.Count(source => source.Candidates.Count > 1),
            sources.Count - sourced);
    }
}

/// <summary>
///     Traces text on screen back to the strings files under <c>src/geoDC.App/Strings</c>, for the <c>source</c> verb,
///     through the find-text tool's <see cref="TextLookup" />, so it reads and matches the files as
///     <c>geodc-find-text</c> and the string-resource convention test do.
/// </summary>
/// <param name="lookup">The strings files and the source that reads them.</param>
public sealed class TextSourceCatalog(TextLookup lookup)
{
    /// <summary>
    ///     Reads the strings files and the source of the geoDC repository that holds
    ///     <paramref name="workingDirectory" />.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>no-repository</c> when it is not inside a geoDC repository, or <c>strings-unreadable</c> when a strings
    ///     file holds a line the catalog does not understand; both exit 1.
    /// </exception>
    public static TextSourceCatalog Load(string workingDirectory)
    {
        string root = RepositoryRoot.Find(workingDirectory) ??
            throw new DriverException("no-repository",
                $"source reads the strings files from the geoDC repository, but no {RepositoryRoot.Marker} was found " +
                $"above '{workingDirectory}'. Run the UI driver with geoDC as its working directory.", 1);
        try
        {
            return new TextSourceCatalog(TextLookup.Load(root));
        }
        catch (InvalidDataException exception)
        {
            throw new DriverException("strings-unreadable", exception.Message, 1);
        }
    }

    /// <summary>
    ///     Every distinct text the walked <paramref name="roots" /> show, in document order: each element's Name, and
    ///     the value of its ValuePattern when that differs. A text with no letter in it (a number, a glyph) is skipped,
    ///     since no strings entry is that alone. <paramref name="contains" />, when given, keeps only the texts that
    ///     contain it, ignoring case.
    /// </summary>
    public static IReadOnlyList<ShownText> TextsIn(IEnumerable<UiaNodeSnapshot> roots, string? contains)
    {
        List<ShownText> texts = [];
        Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> path = [];
        foreach (UiaNodeSnapshot root in roots)
        {
            Visit(root, contains, path, texts, indexes, seen);
        }

        return texts;
    }

    /// <summary>
    ///     The strings-file texts that match the whole of <paramref name="shown" />, closest first (see
    ///     <see cref="TextLookup.Find" />).
    /// </summary>
    public TextSource Look(ShownText shown)
    {
        return new TextSource(shown, lookup.Find(shown.Text));
    }

    private static void Visit(UiaNodeSnapshot node, string? contains, List<string> path, List<ShownText> texts,
        Dictionary<string, int> indexes, HashSet<string> seen)
    {
        // An element reached through two roots (an owned popup is a window and its owner's child) counts once.
        if (node.RuntimeId is not { Length: > 0 } runtimeId || seen.Add(runtimeId))
        {
            Add(node.Name, node, contains, path, texts, indexes);
            if (node.State.Value is { } value && value != node.Name)
            {
                Add(value, node, contains, path, texts, indexes);
            }
        }

        bool pushed = false;
        if (node.AutomationId is { Length: > 0 } automationId)
        {
            path.Add(automationId);
            pushed = true;
        }

        foreach (UiaNodeSnapshot child in node.Children)
        {
            Visit(child, contains, path, texts, indexes, seen);
        }

        if (pushed)
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void Add(string text, UiaNodeSnapshot node, string? contains, List<string> path,
        List<ShownText> texts, Dictionary<string, int> indexes)
    {
        string trimmed = text.Trim();
        if (!trimmed.Any(char.IsLetter) ||
            (contains is not null && !trimmed.Contains(contains, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (indexes.TryGetValue(trimmed, out int index))
        {
            texts[index] = texts[index] with { Elements = texts[index].Elements + 1 };
            return;
        }

        indexes[trimmed] = texts.Count;
        texts.Add(new ShownText(trimmed, node.ControlType, node.AutomationId, [.. path], 1));
    }
}