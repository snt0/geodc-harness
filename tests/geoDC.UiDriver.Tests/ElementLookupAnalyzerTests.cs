using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class ElementLookupAnalyzerTests
{
    private static ElementNotFoundFacts Facts(string requestedId, IReadOnlyCollection<string> searchedIds,
        string? scopeId = null, bool mainWindowOnly = false, IReadOnlyList<string>? elsewhere = null,
        IReadOnlyList<string>? scrollContainerIds = null)
    {
        return new ElementNotFoundFacts(requestedId, "--id", scopeId, mainWindowOnly, searchedIds, elsewhere ?? [],
            scrollContainerIds ?? []);
    }

    private static IReadOnlyList<string> ClosestIds(string requestedId, params string[] searchedIds)
    {
        return [.. ElementLookupAnalyzer.RankClosest(requestedId, searchedIds).Select(entry => entry.Id)];
    }

    [Test]
    public void Typo_SuggestsTheIntendedId()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(Facts("SaveButon",
            ["CancelButton", "SaveButton", "SaveAsButton", "ThemeToggleButton", "SavePanel"]));

        Assert.Multiple(() =>
        {
            Assert.That(hints.Closest, Is.EqualTo(new[] { "SaveButton", "SaveAsButton" }));
            Assert.That(hints.Hint, Is.EqualTo("use --id SaveButton"));
            Assert.That(hints.Elsewhere, Is.Empty);
            Assert.That(hints.Matches, Is.Empty);
        });
    }

    [Test]
    public void Closest_KeepsAtMostFive_TyposFirst_InStableOrder()
    {
        IReadOnlyList<string> closest = ClosestIds("SaveButton",
            "SaveButton", "SaveButon", "SaveButtons", "SavedButton", "SaveBtn", "SaveAsButton", "SaveButtonPanel",
            "CaveButton", "SafeButton");

        // Every distance-1 typo outranks the distance-2 SaveAsButton and the overlap-only SaveBtn and
        // SaveButtonPanel. Within one distance, more shared prefix + suffix ranks first, then ordinal id.
        Assert.That(closest,
            Is.EqualTo(new[] { "SaveButtons", "SavedButton", "CaveButton", "SafeButton", "SaveButon" }));
    }

    [Test]
    public void Closest_NeverIncludesTheRequestedIdItself_AndDeduplicates()
    {
        Assert.That(ClosestIds("SaveButton", "SaveButton", "SaveButon", "SaveButon"),
            Is.EqualTo(new[] { "SaveButon" }));
    }

    [Test]
    public void Closest_CaseOnlyDifference_RanksFirst()
    {
        Assert.That(ClosestIds("savebutton", "SaveButtons", "SaveButton"),
            Is.EqualTo(new[] { "SaveButton", "SaveButtons" }));
    }

    [Test]
    public void Closest_SharedPrefixAndSuffix_RelateIdsBeyondTypoDistance()
    {
        // CancelButton shares only the role suffix, which covers less than two thirds of the request.
        Assert.That(ClosestIds("ApproveButton", "ArchiveButton", "CancelButton", "ApproveAttemptButton", "AproveButton"),
            Is.EqualTo(new[] { "AproveButton", "ApproveAttemptButton", "ArchiveButton" }));
    }

    [Test]
    public void Closest_UnrelatedIds_AreLeftOut()
    {
        Assert.That(ClosestIds("SaveButon", "ThemeToggleButton", "LandingTab", "OkButton"), Is.Empty);
    }

    [Test]
    public void IdOnlyInDetachedPopup_UnderMainWindow_SuggestsItsWindow()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(Facts("ApproveAttemptButton",
            ["SaveButton", "CalibrationTab"], mainWindowOnly: true, elsewhere: ["title:Approve attempt"]));

        Assert.Multiple(() =>
        {
            Assert.That(hints.Elsewhere, Is.EqualTo(new[] { "title:Approve attempt" }));
            Assert.That(hints.Hint, Does.StartWith("drop --main-window:"));
            Assert.That(hints.Hint, Does.Contain("--window \"title:Approve attempt\""));
        });
    }

    [Test]
    public void IdOutsideScope_SuggestsDroppingTheScope_EvenWhenATypoIsClose()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(Facts("SaveButton", ["SaveButon"],
            scopeId: "PopupFooterPanel", elsewhere: ["main"]));

        Assert.Multiple(() =>
        {
            Assert.That(hints.Closest, Is.EqualTo(new[] { "SaveButon" }));
            Assert.That(hints.Hint, Does.StartWith("drop --scope PopupFooterPanel:"));
            Assert.That(hints.Hint, Does.Contain("--window main"));
        });
    }

    [Test]
    public void NoResemblingId_InScrollableContent_SuggestsScrollingForAVirtualizedItem()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(Facts("Row42Button", ["AttemptList", "SaveButton"],
            scrollContainerIds: ["AttemptList", "PageScrollViewer"]));

        Assert.Multiple(() =>
        {
            Assert.That(hints.Closest, Is.Empty);
            Assert.That(hints.Hint, Does.StartWith("try scroll --id AttemptList"));
            Assert.That(hints.Hint, Does.Contain("virtualized"));
        });
    }

    [Test]
    public void TypoOutranksVirtualizationHint()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(Facts("SaveButon", ["SaveButton"],
            scrollContainerIds: ["AttemptList"]));

        Assert.That(hints.Hint, Is.EqualTo("use --id SaveButton"));
    }

    [Test]
    public void MissingScope_NamesTheScopeOption()
    {
        ElementLookupHints hints = ElementLookupAnalyzer.NotFound(new ElementNotFoundFacts("PopupFootrPanel", "--scope",
            null, false, ["PopupFooterPanel", "PopupPanel"], [], []));

        Assert.That(hints.Hint, Is.EqualTo("use --scope PopupFooterPanel"));
    }

    [Test]
    public void NothingKnown_PointsAtTree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ElementLookupAnalyzer.NotFound(Facts("ZzzPanel", ["SaveButton"])).Hint,
                Does.Contain("tree --depth 4"));
            Assert.That(ElementLookupAnalyzer.NotFound(Facts("ZzzPanel", ["SaveButton"], scopeId: "PopupPanel")).Hint,
                Is.EqualTo("run tree --id PopupPanel to list the AutomationIds under the scope"));
        });
    }

    [Test]
    public void WindowSelector_PrefersMain_ThenUniqueTitle_ThenHandle()
    {
        string[] titles = ["geoDC — driver", "Approve attempt", "Approve", ""];

        Assert.Multiple(() =>
        {
            Assert.That(ElementLookupAnalyzer.WindowSelector("geoDC — driver", 0x10, true, titles), Is.EqualTo("main"));
            Assert.That(ElementLookupAnalyzer.WindowSelector("Approve attempt", 0x20, false, titles),
                Is.EqualTo("title:Approve attempt"));
            // "Approve" is also a substring of "Approve attempt", so title:Approve would match two windows.
            Assert.That(ElementLookupAnalyzer.WindowSelector("Approve", 0x30, false, titles),
                Is.EqualTo("handle:0x00000030"));
            Assert.That(ElementLookupAnalyzer.WindowSelector("", 0x40, false, titles), Is.EqualTo("handle:0x00000040"));
        });
    }

    [Test]
    public void AmbiguousMatches_EachGetTheAncestorThatSelectsThemAlone()
    {
        MatchAncestry[] matches =
        [
            new("SaveButton (Button, CalibrationPage > CommandBarPanel)", ["CommandBarPanel", "CalibrationPage"]),
            new("SaveButton (Button, CalibrationPage > PopupPanel > PopupFooterPanel)",
                ["PopupFooterPanel", "PopupPanel", "CalibrationPage"])
        ];

        ElementLookupHints hints = ElementLookupAnalyzer.Ambiguous(matches, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(hints.Matches.Select(match => match.Scope),
                Is.EqualTo(new[] { "CommandBarPanel", "PopupFooterPanel" }));
            Assert.That(hints.Matches.Select(match => match.Element), Is.EqualTo(matches.Select(m => m.Element)));
            Assert.That(hints.Hint, Does.StartWith("use --scope CommandBarPanel"));
            Assert.That(hints.Closest, Is.Empty);
        });
    }

    [Test]
    public void AmbiguousMatches_SkipAncestorsThatAreNotUnique()
    {
        // FooterPanel separates the second match from the first, but it repeats elsewhere, so --scope FooterPanel
        // would itself be ambiguous. The next unique separating ancestor is used instead.
        MatchAncestry[] matches =
        [
            new("OkButton #1", ["FooterPanel", "PageA"]),
            new("OkButton #2", ["FooterPanel2", "PopupPanel", "Root"]),
            new("OkButton #3", ["SharedTemplatePart", "PopupPanel2", "Root"])
        ];
        HashSet<string> unique = ["PageA", "PopupPanel", "PopupPanel2"];

        IReadOnlyList<ScopedMatch> scoped = ElementLookupAnalyzer.ScopeMatches(matches, unique.Contains);

        Assert.That(scoped.Select(match => match.Scope), Is.EqualTo(new[] { "PageA", "PopupPanel", "PopupPanel2" }));
    }

    [Test]
    public void AmbiguousMatches_WithNoSeparatingAncestor_SayHowToFixIt()
    {
        MatchAncestry[] matches =
        [
            new("ItemButton #1", ["ItemsList", "Page"]),
            new("ItemButton #2", ["ItemsList", "Page"])
        ];

        ElementLookupHints hints = ElementLookupAnalyzer.Ambiguous(matches, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(hints.Matches.Select(match => match.Scope), Is.EqualTo(new string?[] { null, null }));
            Assert.That(hints.Hint, Does.StartWith("no unique ancestor AutomationId separates the matches"));
        });
    }

    [Test]
    public void AmbiguousMatches_AreCapped_ButEveryMatchCountsAsShared()
    {
        MatchAncestry[] matches =
        [
            .. Enumerable.Range(0, 12).Select(index => new MatchAncestry($"Cell #{index}", [$"Row{index}", "Grid"]))
        ];

        IReadOnlyList<ScopedMatch> scoped = ElementLookupAnalyzer.ScopeMatches(matches, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(scoped, Has.Count.EqualTo(ElementLookupAnalyzer.MatchLimit));
            Assert.That(scoped.Select(match => match.Scope), Is.EqualTo(Enumerable.Range(0, 10).Select(i => $"Row{i}")));
        });
    }

    [TestCase(true, true, "AttemptList", "'X' is offscreen inside AttemptList; run scrollto --id X (or scroll --id AttemptList) and retry")]
    [TestCase(true, true, null, "'X' is offscreen inside a scroll container; run scrollto --id X and retry")]
    [TestCase(true, false, null, "'X' is offscreen outside any scroll container; it may sit on an unselected tab, a collapsed expander, or a hidden panel; reveal that first")]
    [TestCase(false, false, null, "'X' has zero size; it or a parent is likely collapsed or hidden")]
    public void NotReadyHint_PointsAtWhatHidesTheElement(bool offscreen, bool insideScroll, string? scrollId,
        string expected)
    {
        Assert.That(ElementLookupAnalyzer.NotReadyHint("X", offscreen, insideScroll, scrollId), Is.EqualTo(expected));
    }

    [TestCase("AttemptList", "'X' is offscreen inside AttemptList; run scrollto --id X --scope Panel (or scroll --id AttemptList) and retry")]
    [TestCase(null, "'X' is offscreen inside a scroll container; run scrollto --id X --scope Panel and retry")]
    public void NotReadyHint_KeepsTheScopeTheElementWasResolvedUnder(string? scrollId, string expected)
    {
        Assert.That(ElementLookupAnalyzer.NotReadyHint("X", true, true, scrollId, "Panel"), Is.EqualTo(expected));
    }
}