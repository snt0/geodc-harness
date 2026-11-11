using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class WaitConditionsTests
{
    private static readonly ObservedElementState LaidOut = new(true, false, 120, 30, true, "Save");

    private static WaitConditions Parse(params string[] options)
    {
        return WaitConditions.Parse(new CliArgs(["wait", "--id", "X", .. options]));
    }

    private static DriverException? ParseFails(params string[] options)
    {
        return Assert.Throws<DriverException>(() => Parse(options));
    }

    [Test]
    public void NoFlags_MeansReady_AndNeedsLayout()
    {
        WaitConditions conditions = Parse();

        Assert.Multiple(() =>
        {
            Assert.That(conditions.IsReady, Is.True);
            Assert.That(conditions.Labels, Is.EqualTo(new[] { "ready" }));
            Assert.That(conditions.IsSatisfiedBy(LaidOut), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Offscreen = true }), Is.False);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Height = 0 }), Is.False);
            Assert.That(conditions.IsSatisfiedBy(ObservedElementState.Absent), Is.False);
        });
    }

    [Test]
    public void Gone_IsMetByAbsenceOrOffscreenZeroBounds()
    {
        WaitConditions gone = Parse("--gone");

        Assert.Multiple(() =>
        {
            Assert.That(gone.Evaluate([]), Is.EqualTo(WaitOutcome.Met));
            Assert.That(gone.Evaluate([ObservedElementState.Absent]), Is.EqualTo(WaitOutcome.Met));
            Assert.That(gone.Evaluate([LaidOut with { Offscreen = true, Width = 0, Height = 0 }]),
                Is.EqualTo(WaitOutcome.Met));
            Assert.That(gone.Evaluate([LaidOut]), Is.EqualTo(WaitOutcome.Unmet));
            // Scrolled out of view still occupies space: not gone.
            Assert.That(gone.Evaluate([LaidOut with { Offscreen = true }]), Is.EqualTo(WaitOutcome.Unmet));
        });
    }

    [Test]
    public void Gone_WithSeveralMatches_NeedsAllGone()
    {
        WaitConditions gone = Parse("--gone");
        ObservedElementState leftover = LaidOut with { Offscreen = true, Width = 0, Height = 0 };

        Assert.Multiple(() =>
        {
            Assert.That(gone.Evaluate([leftover, leftover]), Is.EqualTo(WaitOutcome.Met));
            Assert.That(gone.Evaluate([leftover, LaidOut]), Is.EqualTo(WaitOutcome.Unmet));
        });
    }

    [Test]
    public void StateConditions_WithSeveralPresentMatches_AreAmbiguous()
    {
        Assert.That(Parse("--enabled").Evaluate([LaidOut, LaidOut]), Is.EqualTo(WaitOutcome.Ambiguous));
    }

    [Test]
    public void UnreadableElement_IsNeitherGoneNorReady_AndMeetsNoState()
    {
        ObservedElementState unreadable = ObservedElementState.UnreadableNow;

        Assert.Multiple(() =>
        {
            Assert.That(Parse("--gone").Evaluate([unreadable]), Is.EqualTo(WaitOutcome.Unmet),
                "a failed read must not count as the element having gone");
            Assert.That(WaitConditions.Ready.Evaluate([unreadable]), Is.EqualTo(WaitOutcome.Unmet));
            Assert.That(Parse("--disabled").Evaluate([unreadable]), Is.EqualTo(WaitOutcome.Unmet));
            Assert.That(unreadable.Describe(), Is.EqualTo("present, state unreadable"));
        });
    }

    [Test]
    public void StateConditions_OnAbsentElement_AreUnmet()
    {
        Assert.That(Parse("--disabled").Evaluate([]), Is.EqualTo(WaitOutcome.Unmet));
    }

    [Test]
    public void EnabledAndDisabled_TestIsEnabled_WithoutRequiringOnscreen()
    {
        ObservedElementState scrolledAway = LaidOut with { Offscreen = true };

        Assert.Multiple(() =>
        {
            Assert.That(Parse("--enabled").IsSatisfiedBy(scrolledAway), Is.True);
            Assert.That(Parse("--enabled").IsSatisfiedBy(LaidOut with { Enabled = false }), Is.False);
            Assert.That(Parse("--disabled").IsSatisfiedBy(LaidOut with { Enabled = false }), Is.True);
            Assert.That(Parse("--disabled").IsSatisfiedBy(LaidOut), Is.False);
        });
    }

    [Test]
    public void ValueMatches_PrefersValuePattern_OverName()
    {
        WaitConditions conditions = Parse("--value-matches", "^12\\.5");

        Assert.Multiple(() =>
        {
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "Heading", Value = "12.50" }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "12.5", Value = "" }), Is.False);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "12.5 deg", Value = null }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "12.5 deg", ValueUnreadable = true }), Is.False,
                "a failed Value read must not fall back to Name");
            Assert.That(conditions.Labels, Is.EqualTo(new[] { "value-matches /^12\\.5/" }));
        });
    }

    [Test]
    public void ValueMatches_InvalidRegex_IsBadArguments()
    {
        Assert.That(ParseFails("--value-matches", "(unclosed")?.Code, Is.EqualTo("bad-arguments"));
    }

    [TestCase("on", "on", true)]
    [TestCase("ON", "on", true)]
    [TestCase("off", "on", false)]
    [TestCase("indeterminate", "indeterminate", true)]
    public void Toggled_ComparesToggleState(string requested, string observed, bool expected)
    {
        Assert.That(Parse("--toggled", requested).IsSatisfiedBy(LaidOut with { Toggle = observed }),
            Is.EqualTo(expected));
    }

    [Test]
    public void Toggled_RejectsUnknownState_AndNeedsTogglePattern()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParseFails("--toggled", "maybe")?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(Parse("--toggled", "off").IsSatisfiedBy(LaidOut), Is.False);
        });
    }

    [Test]
    public void SelectedAndExpanded_TestTheirPatterns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Parse("--selected").IsSatisfiedBy(LaidOut with { Selected = true }), Is.True);
            Assert.That(Parse("--selected").IsSatisfiedBy(LaidOut with { Selected = false }), Is.False);
            Assert.That(Parse("--selected").IsSatisfiedBy(LaidOut), Is.False);
            Assert.That(Parse("--expanded").IsSatisfiedBy(LaidOut with { Expansion = "expanded" }), Is.True);
            Assert.That(Parse("--expanded").IsSatisfiedBy(LaidOut with { Expansion = "partially-expanded" }),
                Is.False);
        });
    }

    [Test]
    public void StateConditions_CombineWithAnd()
    {
        WaitConditions conditions = Parse("--enabled", "--toggled", "on");

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Labels, Is.EqualTo(new[] { "enabled", "toggled on" }));
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Toggle = "on" }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Toggle = "on", Enabled = false }), Is.False);
        });
    }

    [Test]
    public void Exists_NeedsPresence_NotLayout()
    {
        WaitConditions exists = Parse("--exists");

        Assert.Multiple(() =>
        {
            Assert.That(exists.IsReady, Is.False);
            Assert.That(exists.Labels, Is.EqualTo(new[] { "exists" }));
            Assert.That(exists.Evaluate([LaidOut with { Offscreen = true, Width = 0, Height = 0 }]),
                Is.EqualTo(WaitOutcome.Met));
            Assert.That(exists.Evaluate([]), Is.EqualTo(WaitOutcome.Unmet));
            Assert.That(exists.Evaluate([ObservedElementState.UnreadableNow]), Is.EqualTo(WaitOutcome.Met),
                "found is enough for --exists, even when its state could not be read");
            Assert.That(Parse("--exists", "--enabled").Evaluate([ObservedElementState.UnreadableNow]),
                Is.EqualTo(WaitOutcome.Unmet));
        });
    }

    [Test]
    public void Value_IsExact_AndFallsBackToName_LikeValueMatches()
    {
        WaitConditions conditions = Parse("--value", "12.5");

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Labels, Is.EqualTo(new[] { "value \"12.5\"" }));
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Value = "12.5" }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Value = "12.50" }), Is.False);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "12.5", Value = null }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Value = "12.5", ValueUnreadable = true }), Is.False);
        });
    }

    [Test]
    public void Name_IsExact_AndIgnoresValue()
    {
        WaitConditions conditions = Parse("--name", "Save");

        Assert.Multiple(() =>
        {
            Assert.That(conditions.Labels, Is.EqualTo(new[] { "name \"Save\"" }));
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Value = "other" }), Is.True);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = "save" }), Is.False);
            Assert.That(conditions.IsSatisfiedBy(LaidOut with { Name = null }), Is.False);
        });
    }

    [Test]
    public void ContradictoryFlags_AreBadArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParseFails("--enabled", "--disabled")?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(ParseFails("--gone", "--enabled")?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(ParseFails("--gone", "--value-matches", "x")?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(ParseFails("--gone", "--exists")?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(ParseFails("--value", "x", "--value-matches", "x")?.Code, Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void BooleanFlags_DoNotSwallowTheNextOption()
    {
        CliArgs cli = new(["wait", "--gone", "--id", "PopupPanel", "--timeout", "2"]);

        Assert.Multiple(() =>
        {
            Assert.That(cli.Value("--id"), Is.EqualTo("PopupPanel"));
            Assert.That(WaitConditions.Parse(cli).IsGone, Is.True);
            Assert.That(cli.Timeout(5), Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void Describe_ReportsOnlyPatternsTheElementHas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ObservedElementState.Absent.Describe(), Is.EqualTo("absent"));
            Assert.That(LaidOut.Describe(), Is.EqualTo("enabled, onscreen, 120x30, name=\"Save\""));
            Assert.That((LaidOut with { Value = "4", Toggle = "off", Selected = false, Expansion = "collapsed" })
                    .Describe(),
                Is.EqualTo("enabled, onscreen, 120x30, value=\"4\", name=\"Save\", toggle=off, unselected, collapsed"));
        });
    }
}