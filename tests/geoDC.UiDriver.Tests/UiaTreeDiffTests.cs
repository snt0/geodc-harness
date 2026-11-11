using System.Drawing;
using System.Text.Json;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class UiaTreeDiffTests
{
    private static UiaNodeSnapshot Node(string controlType, string? id, string name = "",
        UiaNodeState? state = null, params UiaNodeSnapshot[] children)
    {
        return new UiaNodeSnapshot(controlType, id, name, new Rectangle(10, 20, 80, 24), true, false,
            state ?? UiaNodeState.None, [], children);
    }

    private static UiaNodeSnapshot Switch(string id, string toggled)
    {
        return Node("CheckBox", id, id, new UiaNodeState(Toggled: toggled));
    }

    /// <summary>A Sample-gallery-shaped window: navigation, a page with two switches, and a status text without an id.</summary>
    private static UiaNodeSnapshot Gallery(string gridToggle = "off", params UiaNodeSnapshot[] extraPageChildren)
    {
        return Node("Window", "GalleryWindow", "geoDC UI Sample", null,
            Node("List", "NavigationList", "", null,
                Node("ListItem", null, "Inputs", new UiaNodeState(Selected: true)),
                Node("ListItem", null, "Overlays")),
            Node("Pane", "InputsPage", "", null,
                [
                    Node("Pane", null, "", null,
                        Switch("ShowGridToggle", gridToggle),
                        Switch("SnapToggle", "on")),
                    Node("Text", null, "2 switches"),
                    .. extraPageChildren
                ]));
    }

    private static UiaNodeSnapshot Popup()
    {
        return Node("Window", "PopupWindow", "Popup", null,
            Node("Pane", "PopupPanel", "", null,
                Node("Text", "PopupTitleBadge", "Blocking popup"),
                Node("Pane", "PopupFooterPanel", "", null,
                    Node("Button", "CancelButton", "Cancel"),
                    Node("Button", "OkButton", "OK"))));
    }

    private static UiaTreeDiff Compare(UiaNodeSnapshot previous, UiaNodeSnapshot current, bool bounds = false)
    {
        return UiaTreeDiff.Compare([previous], [current], bounds);
    }

    [Test]
    public void IdenticalSnapshots_HaveNoDifferences()
    {
        Assert.That(Compare(Gallery(), Gallery()).IsEmpty, Is.True);
    }

    [Test]
    public void TogglingASwitch_IsExactlyOneChangedNode_ListingOnlyToggled()
    {
        UiaTreeDiff diff = Compare(Gallery("off"), Gallery("on"));

        Assert.Multiple(() =>
        {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Removed, Is.Empty);
            Assert.That(diff.Changed, Has.Count.EqualTo(1));
            UiaDiffChange change = diff.Changed[0];
            Assert.That(change.Id, Is.EqualTo("ShowGridToggle"));
            Assert.That(change.Role, Is.EqualTo("CheckBox"));
            Assert.That(change.Path, Is.EqualTo(new[] { "GalleryWindow", "InputsPage" }));
            Assert.That(change.Changes.Keys, Is.EqualTo(new[] { "toggled" }));
            Assert.That(change.Changes["toggled"], Is.EqualTo(new UiaFieldChange("off", "on")));
        });
    }

    [Test]
    public void OpeningAPopup_ReportsThePopupSubtreeAsOneAddedRoot()
    {
        UiaTreeDiff diff = UiaTreeDiff.Compare([Gallery()], [Gallery(), Popup()], false);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Removed, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Added, Has.Count.EqualTo(1));
            Assert.That(diff.Added[0].Id, Is.EqualTo("PopupWindow"));
            Assert.That(diff.Added[0].Path, Is.Empty);
            Assert.That(diff.Added[0].Descendants, Is.EqualTo(5));
        });
    }

    [Test]
    public void ClosingAnInPagePanel_ReportsOnlyItsRootAsRemoved_WithItsPath()
    {
        UiaNodeSnapshot panel = Node("Pane", "DetailsPanel", "", null,
            Node("Edit", "NameInput", "", new UiaNodeState(Value: "abc")),
            Node("Button", "ApplyButton", "Apply"));

        UiaTreeDiff diff = Compare(Gallery("off", panel), Gallery());

        Assert.Multiple(() =>
        {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Removed, Has.Count.EqualTo(1));
            Assert.That(diff.Removed[0].Id, Is.EqualTo("DetailsPanel"));
            Assert.That(diff.Removed[0].Path, Is.EqualTo(new[] { "GalleryWindow", "InputsPage" }));
            Assert.That(diff.Removed[0].Descendants, Is.EqualTo(2));
        });
    }

    [Test]
    public void SiblingsWithAutomationIds_MatchByIdWhateverTheirOrder()
    {
        UiaNodeSnapshot before = Node("Pane", "Root", "", null, Switch("A", "on"), Switch("B", "off"));
        UiaNodeSnapshot after = Node("Pane", "Root", "", null, Switch("B", "off"), Switch("A", "on"));

        Assert.That(Compare(before, after).IsEmpty, Is.True);
    }

    [Test]
    public void SiblingsWithoutIds_MatchByControlTypeAndName_ThenOccurrence()
    {
        UiaNodeSnapshot before = Node("List", "Items", "", null,
            Node("ListItem", null, "Stage 1"),
            Node("ListItem", null, "Stage 2"),
            Node("ListItem", null, "Stage 2", new UiaNodeState(Selected: true)));
        UiaNodeSnapshot after = Node("List", "Items", "", null,
            Node("ListItem", null, "Stage 2"),
            Node("ListItem", null, "Stage 2"),
            Node("ListItem", null, "Stage 1"));

        UiaTreeDiff diff = Compare(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Removed, Is.Empty);
            Assert.That(diff.Changed, Has.Count.EqualTo(1), "the second 'Stage 2' lost its selection");
            Assert.That(diff.Changed[0].Name, Is.EqualTo("Stage 2"));
            Assert.That(diff.Changed[0].Changes["selected"], Is.EqualTo(new UiaFieldChange(true, false)));
        });
    }

    [Test]
    public void RepeatedAutomationIds_MatchInOrderOfOccurrence()
    {
        UiaNodeSnapshot before = Node("Pane", "Root", "", null,
            Node("Button", "RemoveButton", "Remove", null),
            Node("Button", "RemoveButton", "Remove", null));
        UiaNodeSnapshot after = Node("Pane", "Root", "", null,
            Node("Button", "RemoveButton", "Remove", null),
            Node("Button", "RemoveButton", "Remove", null) with { IsEnabled = false });

        UiaTreeDiff diff = Compare(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Changed, Has.Count.EqualTo(1));
            Assert.That(diff.Changed[0].Changes["enabled"], Is.EqualTo(new UiaFieldChange(true, false)));
        });
    }

    [Test]
    public void ARenamedNodeWithoutAnId_IsANameChange_WhenItIsTheOnlyUnmatchedOneOfItsType()
    {
        UiaTreeDiff diff = Compare(Gallery(), Gallery() with
        {
            Children = [Gallery().Children[0], Gallery().Children[1] with
            {
                Children = [Gallery().Children[1].Children[0], Node("Text", null, "3 switches")]
            }]
        });

        Assert.Multiple(() =>
        {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Removed, Is.Empty);
            Assert.That(diff.Changed, Has.Count.EqualTo(1));
            Assert.That(diff.Changed[0].Changes.Keys, Is.EqualTo(new[] { "name" }));
            Assert.That(diff.Changed[0].Changes["name"], Is.EqualTo(new UiaFieldChange("2 switches", "3 switches")));
        });
    }

    [Test]
    public void UnmatchedNodesWithoutIds_StayAddedAndRemoved_WhenTheirCountsDiffer()
    {
        UiaNodeSnapshot before = Node("Pane", "Root", "", null, Node("Text", null, "Loading"));
        UiaNodeSnapshot after = Node("Pane", "Root", "", null, Node("Text", null, "Rows: 3"), Node("Text", null, "Done"));

        UiaTreeDiff diff = Compare(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Removed.Select(entry => entry.Name), Is.EqualTo(new[] { "Loading" }));
            Assert.That(diff.Added.Select(entry => entry.Name), Is.EqualTo(new[] { "Rows: 3", "Done" }));
        });
    }

    [Test]
    public void ANodeWithAnIdUnderAnotherParent_IsRemovedAndAdded()
    {
        UiaNodeSnapshot before = Node("Pane", "Root", "", null,
            Node("Pane", "LeftPanel", "", null, Node("Button", "MoveButton", "Move")),
            Node("Pane", "RightPanel"));
        UiaNodeSnapshot after = Node("Pane", "Root", "", null,
            Node("Pane", "LeftPanel"),
            Node("Pane", "RightPanel", "", null, Node("Button", "MoveButton", "Move")));

        UiaTreeDiff diff = Compare(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(diff.Removed.Single().Path, Is.EqualTo(new[] { "Root", "LeftPanel" }));
            Assert.That(diff.Added.Single().Path, Is.EqualTo(new[] { "Root", "RightPanel" }));
            Assert.That(diff.Changed, Is.Empty);
        });
    }

    [Test]
    public void AChangedNode_ListsOnlyItsChangedFields_InTheFixedOrder()
    {
        UiaNodeSnapshot before = Node("ComboBox", "UnitsCombo", "Units",
            new UiaNodeState(Value: "m", Expanded: "collapsed"));
        UiaNodeSnapshot after = before with
        {
            Name = "Units (metric)",
            IsOffscreen = true,
            State = new UiaNodeState(Value: "ft", Expanded: "expanded")
        };

        UiaDiffChange change = Compare(before, after).Changed.Single();

        Assert.Multiple(() =>
        {
            Assert.That(change.Changes.Keys, Is.EqualTo(new[] { "name", "value", "expanded", "offscreen" }));
            Assert.That(change.Changes["value"], Is.EqualTo(new UiaFieldChange("m", "ft")));
            Assert.That(change.Changes["expanded"], Is.EqualTo(new UiaFieldChange("collapsed", "expanded")));
            Assert.That(change.Changes["offscreen"], Is.EqualTo(new UiaFieldChange(false, true)));
            Assert.That(change.Name, Is.EqualTo("Units (metric)"));
        });
    }

    [Test]
    public void AControlTypeChangeUnderTheSameId_IsReportedAsARoleChange()
    {
        UiaNodeSnapshot before = Node("Button", "StatusBadge", "Ready");
        UiaNodeSnapshot after = before with { ControlType = "Text" };

        UiaDiffChange change = Compare(before, after).Changed.Single();

        Assert.Multiple(() =>
        {
            Assert.That(change.Changes.Keys, Is.EqualTo(new[] { "role" }));
            Assert.That(change.Changes["role"], Is.EqualTo(new UiaFieldChange("Button", "Text")));
            Assert.That(change.Role, Is.EqualTo("Text"));
        });
    }

    [Test]
    public void ScrollAndPatternChanges_AreNotCompared()
    {
        UiaNodeSnapshot before = Node("List", "RowsList", "", new UiaNodeState(Scroll: "h=n/a v=0%"));
        UiaNodeSnapshot after = before with
        {
            State = new UiaNodeState(Scroll: "h=n/a v=40%"),
            Patterns = ["Scroll"],
            RuntimeId = "42,1"
        };

        Assert.That(Compare(before, after).IsEmpty, Is.True);
    }

    [Test]
    public void BoundsChanges_AreLeftOutUnlessRequested()
    {
        UiaNodeSnapshot before = Node("Pane", "Root", "", null, Node("Button", "SaveButton", "Save"));
        UiaNodeSnapshot after = Node("Pane", "Root", "", null,
            Node("Button", "SaveButton", "Save") with { Bounds = new Rectangle(10, 60, 80, 24) });

        UiaTreeDiff withoutBounds = Compare(before, after);
        UiaTreeDiff withBounds = Compare(before, after, true);

        Assert.Multiple(() =>
        {
            Assert.That(withoutBounds.IsEmpty, Is.True);
            Assert.That(withBounds.Changed.Single().Changes["bounds"],
                Is.EqualTo(new UiaFieldChange("10,20 80x24", "10,60 80x24")));
        });
    }

    [Test]
    public void Format_PrintsAddedSubtreesInFull_RemovedRoots_AndChangedFields()
    {
        UiaTreeDiff diff = UiaTreeDiff.Compare([Gallery("off")], [Gallery("on"), Popup()], false);

        string[] lines = diff.Format().TrimEnd().Split(Environment.NewLine);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(7));
            Assert.That(lines[0], Does.StartWith("+ [Window] id: PopupWindow  name: \"Popup\""));
            Assert.That(lines[0], Does.EndWith("path: —"));
            Assert.That(lines[1], Does.StartWith("+   [Pane] id: PopupPanel"));
            Assert.That(lines[6], Is.EqualTo(
                "~ [CheckBox] id: ShowGridToggle  name: \"ShowGridToggle\"  path: GalleryWindow > InputsPage  " +
                "toggled: off -> on"));
        });
    }

    [Test]
    public void Format_RemovedRoot_CountsItsDescendants()
    {
        UiaTreeDiff diff = UiaTreeDiff.Compare([Gallery(), Popup()], [Gallery()], false);

        Assert.That(diff.Format().TrimEnd(), Does.StartWith("- [Window] id: PopupWindow").And.EndWith(
            "path: —  descendants: 5"));
    }

    [Test]
    public void Serialized_EntriesCarryRoleIdNamePathAndCounts_WithoutTheSubtree()
    {
        UiaTreeDiff diff = UiaTreeDiff.Compare([Gallery("off")], [Gallery("on"), Popup()], false);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        string added = JsonSerializer.Serialize(diff.Added, options);
        string changed = JsonSerializer.Serialize(diff.Changed, options);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(
                "[{\"role\":\"Window\",\"id\":\"PopupWindow\",\"name\":\"Popup\",\"path\":[],\"descendants\":5}]"));
            Assert.That(changed, Is.EqualTo(
                "[{\"role\":\"CheckBox\",\"id\":\"ShowGridToggle\",\"name\":\"ShowGridToggle\"," +
                "\"path\":[\"GalleryWindow\",\"InputsPage\"],\"changes\":{\"toggled\":{\"from\":\"off\",\"to\":\"on\"}}}]"));
        });
    }
}