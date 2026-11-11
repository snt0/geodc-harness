using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class TreeAndFindOptionsTests
{
    private static readonly string[] ControlTypes = ["Button", "CheckBox", "Edit", "Window"];

    private static TreeOptions Tree(params string[] options)
    {
        return TreeOptions.Parse(new CliArgs(["tree", .. options]));
    }

    private static FindCriteria Find(params string[] options)
    {
        return FindCriteria.Parse(new CliArgs(["find", .. options]), ControlTypes);
    }

    private static DriverException? Failure(Action parse)
    {
        return Assert.Throws<DriverException>(() => parse());
    }

    [Test]
    public void Tree_DefaultsToDepthFour()
    {
        Assert.That(Tree(), Is.EqualTo(new TreeOptions(false, TreeOptions.DefaultDepth)));
    }

    [Test]
    public void Tree_Interactable_DefaultsToUnlimitedDepth()
    {
        Assert.That(Tree("--interactable"), Is.EqualTo(new TreeOptions(true, null)));
    }

    [Test]
    public void Tree_Interactable_IsAFlag_SoAFollowingDepthStillApplies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Tree("--interactable", "--depth", "6"), Is.EqualTo(new TreeOptions(true, 6)));
            Assert.That(Tree("--depth", "2", "--interactable", "--scope", "PopupPanel"),
                Is.EqualTo(new TreeOptions(true, 2)));
        });
    }

    [Test]
    public void Tree_DiffAndBounds_AreFlags_ThatKeepTheOtherOptions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Tree("--diff"), Is.EqualTo(new TreeOptions(false, TreeOptions.DefaultDepth, true)));
            Assert.That(Tree("--diff", "--interactable", "--bounds", "--scope", "InputsPage"),
                Is.EqualTo(new TreeOptions(true, null, true, true)));
        });
    }

    [Test]
    public void Tree_BoundsWithoutDiff_IsBadArguments()
    {
        Assert.That(Failure(() => Tree("--bounds"))?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void Tree_NegativeDepth_IsBadArguments()
    {
        Assert.That(Failure(() => Tree("--depth", "-1"))?.Code, Is.EqualTo("bad-arguments"));
    }

    [Test]
    public void Find_CanonicalisesRoleAndPattern()
    {
        Assert.That(Find("--role", "button", "--pattern", "selectionitem", "--name", "ok", "--id-prefix", "Popup"),
            Is.EqualTo(new FindCriteria("ok", "Button", "SelectionItem", "Popup")));
    }

    [Test]
    public void Find_WithoutCriteria_IsBadArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Failure(() => Find())?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(Failure(() => Find("--scope", "PopupFooterPanel"))?.Code, Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Find_UnknownRoleOrPattern_ListsTheValidNames()
    {
        DriverException? role = Failure(() => Find("--role", "Buton"));
        DriverException? pattern = Failure(() => Find("--pattern", "Click"));

        Assert.Multiple(() =>
        {
            Assert.That(role?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(role?.Candidates, Is.EqualTo(new[] { "Button", "CheckBox", "Edit", "Window" }));
            Assert.That(pattern?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(pattern?.Candidates, Does.Contain("Invoke").And.Contain("ExpandCollapse"));
        });
    }

    [Test]
    public void Find_RejectsDepthIdAndEmptyValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Failure(() => Find("--role", "Button", "--depth", "3"))?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(Failure(() => Find("--id", "OkButton"))?.Code, Is.EqualTo("bad-arguments"));
            Assert.That(Failure(() => Find("--name", ""))?.Code, Is.EqualTo("bad-arguments"));
        });
    }
}