using System.Drawing;
using System.Text;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class UiaTreeFilterTests
{
    private static readonly string[] ControlTypes = ["Button", "CheckBox", "Edit", "Group", "Pane", "Text", "Window"];

    private static UiaNodeSnapshot Node(string controlType, string? id, string[] patterns,
        params UiaNodeSnapshot[] children)
    {
        return new UiaNodeSnapshot(controlType, id, id ?? string.Empty, new Rectangle(1, 2, 30, 40), true, false,
            UiaNodeState.None, patterns, children, id is null ? null : $"rt-{id}");
    }

    private static UiaNodeSnapshot Named(string controlType, string? id, string name, string[] patterns,
        string? runtimeId = null)
    {
        return new UiaNodeSnapshot(controlType, id, name, Rectangle.Empty, true, false, UiaNodeState.None, patterns,
            [], runtimeId);
    }

    private static string[] Lines(UiaNodeSnapshot root)
    {
        StringBuilder builder = new();
        root.WriteTree(builder);
        return builder.ToString().TrimEnd().Split(Environment.NewLine);
    }

    private static string Outline(UiaNodeSnapshot root, int level = 0)
    {
        StringBuilder builder = new();
        builder.Append(' ', level * 2).AppendLine(root.AutomationId ?? $"({root.ControlType})");
        foreach (UiaNodeSnapshot child in root.Children)
        {
            builder.Append(Outline(child, level + 1));
        }

        return builder.ToString();
    }

    private static FindCriteria Criteria(params string[] options)
    {
        return FindCriteria.Parse(new CliArgs(["find", .. options]), ControlTypes);
    }

    /// <summary>
    ///     A popup-shaped tree: layout panes without ids, an id'd footer holding three buttons (one wrapped in an
    ///     anonymous group), and a text block with an id but no interactable pattern.
    /// </summary>
    private static UiaNodeSnapshot PopupTree()
    {
        return Node("Window", "PopupWindow", [],
            Node("Pane", null, [],
                Node("Pane", "PopupPanel", [],
                    Node("Text", "PopupTitleBadge", []),
                    Node("Pane", null, [],
                        Node("Edit", "NameInput", ["Value"]),
                        Node("Text", null, [])),
                    Node("Pane", "PopupFooterPanel", [],
                        Node("Button", "CancelButton", ["Invoke"]),
                        Node("Group", null, [],
                            Node("Button", "ApplyButton", ["Invoke"])),
                        Node("Button", "OkButton", ["Invoke"])))));
    }

    [Test]
    public void KeepInteractable_KeepsInteractableNodesAndTheirChainToTheNearestId()
    {
        UiaNodeSnapshot pruned = UiaTreeFilter.KeepInteractable(PopupTree());

        Assert.That(Outline(pruned), Is.EqualTo(string.Join(Environment.NewLine,
            "PopupWindow",
            "  PopupPanel",
            "    (Pane)",
            "      NameInput",
            "    PopupFooterPanel",
            "      CancelButton",
            "      (Group)",
            "        ApplyButton",
            "      OkButton",
            string.Empty)));
    }

    [Test]
    public void KeepInteractable_DropsAncestorsAboveTheNearestId()
    {
        UiaNodeSnapshot root = Node("Window", "MainWindow", [],
            Node("Pane", "OuterPanel", [],
                Node("Pane", null, [],
                    Node("Pane", "InnerPanel", [],
                        Node("Pane", null, [],
                            Node("CheckBox", null, ["Toggle"]))))));

        UiaNodeSnapshot pruned = UiaTreeFilter.KeepInteractable(root);

        Assert.That(Outline(pruned), Is.EqualTo(string.Join(Environment.NewLine,
            "MainWindow",
            "  InnerPanel",
            "    (Pane)",
            "      (CheckBox)",
            string.Empty)));
    }

    [Test]
    public void KeepInteractable_InteractableNodeWithItsOwnIdStillShowsItsScope()
    {
        UiaNodeSnapshot root = Node("Window", "MainWindow", [],
            Node("Pane", "HostPanel", [],
                Node("Pane", null, [],
                    Node("Button", "PartButton", ["Invoke"]))));

        UiaNodeSnapshot pruned = UiaTreeFilter.KeepInteractable(root);

        Assert.That(Outline(pruned), Is.EqualTo(string.Join(Environment.NewLine,
            "MainWindow",
            "  HostPanel",
            "    (Pane)",
            "      PartButton",
            string.Empty)));
    }

    [Test]
    public void KeepInteractable_SelectionAndScrollItemAloneAreNotInteractable()
    {
        UiaNodeSnapshot root = Node("Window", "MainWindow", [],
            Node("Pane", "ListPanel", ["Selection"],
                Node("Text", "RowText", ["ScrollItem"])));

        UiaNodeSnapshot pruned = UiaTreeFilter.KeepInteractable(root);

        Assert.Multiple(() =>
        {
            Assert.That(pruned.Children, Is.Empty, "the root is kept even with nothing interactable under it");
            Assert.That(pruned.AutomationId, Is.EqualTo("MainWindow"));
        });
    }

    [Test]
    public void KeepInteractable_ChainWithoutAnyIdEndsAtTheRoot()
    {
        UiaNodeSnapshot root = Node("Window", null, [],
            Node("Pane", null, [],
                Node("Button", null, ["Invoke"])));

        Assert.That(Outline(UiaTreeFilter.KeepInteractable(root)), Is.EqualTo(string.Join(Environment.NewLine,
            "(Window)",
            "  (Pane)",
            "    (Button)",
            string.Empty)));
    }

    [Test]
    public void KeepInteractable_IsSubstantiallySmallerThanTheFullTree()
    {
        UiaNodeSnapshot tree = PopupTree();

        Assert.That(Lines(UiaTreeFilter.KeepInteractable(tree)), Has.Length.LessThan(Lines(tree).Length));
    }

    [Test]
    public void Find_ByRoleUnderTheFooterScope_ReturnsEveryFooterButton()
    {
        UiaNodeSnapshot footer = PopupTree().Children[0].Children[0].Children[2];

        IReadOnlyList<UiaTreeMatch> matches = UiaTreeFilter.Find([footer], Criteria("--role", "button"));

        Assert.Multiple(() =>
        {
            Assert.That(matches.Select(match => match.Node.AutomationId),
                Is.EqualTo(new[] { "CancelButton", "ApplyButton", "OkButton" }));
            Assert.That(matches.Select(match => string.Join(" > ", match.Path)),
                Is.All.EqualTo("PopupFooterPanel"));
        });
    }

    [Test]
    public void Find_BreadcrumbListsAncestorIdsFromTheRoot()
    {
        IReadOnlyList<UiaTreeMatch> matches = UiaTreeFilter.Find([PopupTree()], Criteria("--id-prefix", "apply"));

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].Path, Is.EqualTo(new[] { "PopupWindow", "PopupPanel", "PopupFooterPanel" }));
            Assert.That(matches[0].FormatLine(), Does.StartWith("[Button] id: ApplyButton  name: \"ApplyButton\""));
            Assert.That(matches[0].FormatLine(),
                Does.EndWith("patterns: Invoke  path: PopupWindow > PopupPanel > PopupFooterPanel"));
        });
    }

    [Test]
    public void Find_MatchesTheRootItself_WithAnEmptyPath()
    {
        IReadOnlyList<UiaTreeMatch> matches = UiaTreeFilter.Find([PopupTree()], Criteria("--role", "Window"));

        Assert.Multiple(() =>
        {
            Assert.That(matches.Select(match => match.Node.AutomationId), Is.EqualTo(new[] { "PopupWindow" }));
            Assert.That(matches[0].FormatLine(), Does.EndWith("path: —"));
        });
    }

    [Test]
    public void Find_CombinesCriteriaWithAnd()
    {
        UiaNodeSnapshot root = Node("Window", "MainWindow", [],
            Named("Button", "SaveButton", "Save project", ["Invoke"]),
            Named("Text", "SaveBadge", "Save pending", []),
            Named("Button", "SaveAsButton", "Save as", ["Invoke"]),
            Named("CheckBox", "SaveOnExitToggle", "Save on exit", ["Toggle"]));

        Assert.Multiple(() =>
        {
            Assert.That(UiaTreeFilter.Find([root], Criteria("--name", "SAVE", "--pattern", "invoke"))
                    .Select(match => match.Node.AutomationId),
                Is.EqualTo(new[] { "SaveButton", "SaveAsButton" }));
            Assert.That(UiaTreeFilter.Find([root], Criteria("--name", "save", "--role", "Button", "--id-prefix",
                    "SaveAs"))
                .Select(match => match.Node.AutomationId), Is.EqualTo(new[] { "SaveAsButton" }));
            Assert.That(UiaTreeFilter.Find([root], Criteria("--pattern", "Toggle"))
                .Select(match => match.Node.AutomationId), Is.EqualTo(new[] { "SaveOnExitToggle" }));
        });
    }

    [Test]
    public void Find_IdPrefixNeverMatchesANodeWithoutAnId()
    {
        UiaNodeSnapshot root = Node("Window", null, [], Named("Button", null, "Save", ["Invoke"]));

        Assert.That(UiaTreeFilter.Find([root], Criteria("--id-prefix", "S")), Is.Empty);
    }

    [Test]
    public void Find_ReportsAnElementReachedThroughTwoRootsOnce()
    {
        UiaNodeSnapshot popup = Node("Window", "PopupWindow", [], Node("Button", "OkButton", ["Invoke"]));
        UiaNodeSnapshot main = Node("Window", "MainWindow", [], popup);

        IReadOnlyList<UiaTreeMatch> matches = UiaTreeFilter.Find([main, popup], Criteria("--role", "Button"));

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].Path, Is.EqualTo(new[] { "MainWindow", "PopupWindow" }));
        });
    }

    [Test]
    public void FormatLine_PrintsTheTreeNodeShape()
    {
        UiaNodeSnapshot node = new("CheckBox", null, "Show grid", new Rectangle(10, 20, 80, 24), false, true,
            new UiaNodeState(Toggled: "on"), ["Toggle", "ScrollItem"], []);

        Assert.That(node.FormatLine(), Is.EqualTo(
            "[CheckBox] id: —  name: \"Show grid\"  bounds=(10,20 80x24)  disabled  offscreen  toggled: on  " +
            "patterns: Toggle/ScrollItem"));
    }

    [Test]
    public void WriteTree_IndentsTwoSpacesPerLevelAndCountsLines()
    {
        UiaNodeSnapshot root = Node("Window", "MainWindow", [], Node("Pane", null, [], Node("Text", "A", [])));
        StringBuilder builder = new();

        int lines = root.WriteTree(builder);

        string[] written = builder.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Is.EqualTo(3));
            Assert.That(written[1], Does.StartWith("  [Pane] id: —"));
            Assert.That(written[2], Does.StartWith("    [Text] id: A"));
            Assert.That(written[1], Does.EndWith("patterns: —"));
        });
    }
}