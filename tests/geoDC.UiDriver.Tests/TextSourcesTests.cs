using System.Drawing;
using geoDC.FindText;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     The lookup behind <c>source</c>: which texts a walked tree shows, and which strings-file entries each matches.
/// </summary>
[TestFixture]
public sealed class TextSourcesTests
{
    private const string StringsFile = "src/geoDC.App/Strings/calibration.yaml";

    private static readonly string[] Yaml =
    [
        "pageGate:",
        "  title: No angle sensors mounted",
        "  message:",
        "    match: bool",
        "    cases:",
        "      true: Mount an angle sensor in Survey to calibrate it.",
        "      false: Choose a job from the title bar, or create one in Setup, before calibrating a sensor.",
        "  action: Go to Survey",
        "rows: \"{count:int} rows\"",
        "lowerSurvey: go to survey",
        "survey: Go to Survey",
        "save: Save the calibration"
    ];

    private static UiaNodeSnapshot Node(string controlType, string? id, string name,
        IReadOnlyList<UiaNodeSnapshot>? children = null, string? value = null, string? runtimeId = null)
    {
        return new UiaNodeSnapshot(controlType, id, name, Rectangle.Empty, true, false,
            value is null ? UiaNodeState.None : new UiaNodeState(value), value is null ? [] : ["Value"], children ?? [],
            runtimeId);
    }

    private static ShownText Shown(string text)
    {
        return new ShownText(text, "Text", null, [], 1);
    }

    private static TextSourceCatalog Catalog(IReadOnlyDictionary<string, List<SourceLocation>>? references = null)
    {
        return new TextSourceCatalog(new TextLookup([.. StringCatalog.EntriesIn(StringsFile, Yaml)],
            references ?? new Dictionary<string, List<SourceLocation>>()));
    }

    [Test]
    public void TextsIn_ListsEachTextOnce_InDocumentOrder_WithTheFirstElementThatShowsIt()
    {
        UiaNodeSnapshot tree = Node("Pane", "CalibrationPage", string.Empty,
        [
            Node("Pane", null, "No angle sensors mounted",
            [
                Node("Text", null, "No angle sensors mounted"),
                Node("Text", null, "Choose a job from the title bar."),
                Node("Button", "GoToSurveyButton", "Go to Survey", [Node("Text", null, "Go to Survey")])
            ])
        ]);

        IReadOnlyList<ShownText> texts = TextSourceCatalog.TextsIn([tree], null);

        Assert.That(texts, Is.EqualTo(new[]
        {
            new ShownText("No angle sensors mounted", "Pane", null, ["CalibrationPage"], 2),
            new ShownText("Choose a job from the title bar.", "Text", null, ["CalibrationPage"], 1),
            new ShownText("Go to Survey", "Button", "GoToSurveyButton", ["CalibrationPage"], 2)
        }).Using<ShownText>(SameShownText));
    }

    [Test]
    public void TextsIn_ReadsAValueThatDiffersFromTheName_AndSkipsTextsWithNoLetter()
    {
        UiaNodeSnapshot tree = Node("Pane", "Form", string.Empty,
        [
            Node("Edit", "NameInput", "Calibration name", value: "Pitch"),
            Node("Edit", "OffsetInput", "Offset", value: "Offset"),
            Node("Text", null, "30.003"),
            Node("Text", null, "  —  ")
        ]);

        string[] texts = [.. TextSourceCatalog.TextsIn([tree], null).Select(text => text.Text)];

        Assert.That(texts, Is.EqualTo(new[] { "Calibration name", "Pitch", "Offset" }));
    }

    [Test]
    public void TextsIn_WithAFilter_KeepsOnlyTextsContainingIt_IgnoringCase()
    {
        UiaNodeSnapshot tree = Node("Pane", null, string.Empty,
        [
            Node("Text", null, "Choose a job from the title bar."),
            Node("Text", null, "Go to Survey")
        ]);

        string[] texts = [.. TextSourceCatalog.TextsIn([tree], "CHOOSE A JOB").Select(text => text.Text)];

        Assert.That(texts, Is.EqualTo(new[] { "Choose a job from the title bar." }));
    }

    [Test]
    public void TextsIn_CountsAnElementReachedThroughTwoRootsOnce()
    {
        UiaNodeSnapshot popup = Node("Window", "EditorPopup", "Edit point", runtimeId: "42,1");
        UiaNodeSnapshot owner = Node("Window", "MainWindow", "geoDC", [popup], runtimeId: "42,0");

        IReadOnlyList<ShownText> texts = TextSourceCatalog.TextsIn([owner, popup], null);

        Assert.That(texts.Single(text => text.Text == "Edit point").Elements, Is.EqualTo(1));
    }

    [Test]
    public void Look_TracesAMatchCaseToItsEntryAndCase()
    {
        TextSource source =
            Catalog().Look(Shown("Choose a job from the title bar, or create one in Setup, before calibrating a sensor."));

        Assert.That(source.Candidates.Select(candidate => candidate.Describe()), Is.EqualTo(new[]
        {
            $"{StringsFile}:7 › pageGate.message (case false)  CalibrationText.PageGateMessage"
        }));
    }

    [Test]
    public void Look_ListsEveryEntryWhenTheTextIsAmbiguous_SameCaseFirst_ThenInFileAndLineOrder()
    {
        TextSource source = Catalog().Look(Shown("Go to Survey"));

        Assert.That(source.Candidates.Select(candidate => candidate.Entry.KeyPath),
            Is.EqualTo(new[] { "pageGate.action", "survey", "lowerSurvey" }));
    }

    [Test]
    public void Look_MatchesATextWithItsPlaceholdersFilledIn_AfterWholeMatches()
    {
        TextSource source = Catalog().Look(Shown("3 rows"));

        Assert.Multiple(() =>
        {
            Assert.That(source.Candidates.Select(candidate => candidate.Entry.KeyPath), Is.EqualTo(new[] { "rows" }));
            Assert.That(source.Candidates[0].Match, Is.EqualTo(TextMatch.FilledIn));
            Assert.That(source.Candidates[0].Describe(), Does.EndWith("  filled in"));
        });
    }

    [Test]
    public void Look_DoesNotTraceAShortLabelToEveryTextContainingIt()
    {
        Assert.That(Catalog().Look(Shown("Save")).Candidates, Is.Empty);
    }

    [Test]
    public void Format_NamesTheElement_ThenEachCandidateWithTheLinesThatReadIt()
    {
        Dictionary<string, List<SourceLocation>> references = new()
        {
            ["CalibrationText.PageGateTitle"] = [new SourceLocation("src/geoDC.App/Features/Calibration/Page.xaml", 193)]
        };
        ShownText shown = new("No angle sensors mounted", "Pane", "CalibrationGate", ["CalibrationPage"], 2);

        string text = Catalog(references).Look(shown).Format();

        Assert.That(text, Is.EqualTo(
            "\"No angle sensors mounted\"  [Pane] id: CalibrationGate  ×2  path: CalibrationPage\n" +
            $"  {StringsFile}:2 › pageGate.title  CalibrationText.PageGateTitle\n" +
            "    read at src/geoDC.App/Features/Calibration/Page.xaml:193\n"));
    }

    [Test]
    public void Format_SaysWhenNoEntryReadsAsTheText()
    {
        string text = Catalog().Look(new ShownText("Pitch\nverification", "Text", null, [], 1)).Format();

        Assert.That(text, Is.EqualTo(
            "\"Pitch\\nverification\"  [Text]  path: —\n" +
            "  no strings entry reads as this: it is data, or text built in code\n"));
    }

    [Test]
    public void Summary_CountsSourcedAmbiguousAndUnsourcedTexts()
    {
        TextSourceCatalog catalog = Catalog();
        TextSource[] sources =
        [
            catalog.Look(Shown("Go to Survey")),
            catalog.Look(Shown("No angle sensors mounted")),
            catalog.Look(Shown("Pitch"))
        ];

        Assert.That(TextSourceSummary.Of(sources), Is.EqualTo(new TextSourceSummary(3, 2, 1, 1)));
    }

    [Test]
    public void Load_ReadsTheRepositoryStrings_AndTracesTheCalibrationGateMessage()
    {
        TextSourceCatalog catalog = TextSourceCatalog.Load(TestPaths.GeoDCRoot);

        TextSource source =
            catalog.Look(Shown("Choose a job from the title bar, or create one in Setup, before calibrating a sensor."));

        Assert.That(source.Candidates, Has.Count.EqualTo(1));
        TextCandidate candidate = source.Candidates[0];
        Assert.Multiple(() =>
        {
            Assert.That(candidate.Entry.File, Is.EqualTo(StringsFile));
            Assert.That(candidate.Entry.KeyPath, Is.EqualTo("pageGate.message"));
            Assert.That(candidate.Text.Case, Is.EqualTo("false"));
            Assert.That(candidate.Reads.Select(read => read.File),
                Has.Member("src/geoDC.App/Features/Calibration/CalibrationShellPage.xaml"));
        });
    }

    [Test]
    public void Load_TracesAMenuHeaderShownWithoutItsAccessKeyMarker()
    {
        TextSourceCatalog catalog = TextSourceCatalog.Load(TestPaths.GeoDCRoot);

        TextSource source = catalog.Look(new ShownText("File", "MenuItem", "FileMenu", [], 1));

        Assert.That(source.Candidates.Select(candidate => $"{candidate.Entry.File} › {candidate.Entry.KeyPath}"),
            Has.Member("src/geoDC.App/Strings/shell.yaml › menu.file.header"));
    }

    private static bool SameShownText(ShownText expected, ShownText actual)
    {
        return expected.Text == actual.Text && expected.Role == actual.Role &&
               expected.AutomationId == actual.AutomationId && expected.Path.SequenceEqual(actual.Path) &&
               expected.Elements == actual.Elements;
    }
}