using System.Drawing;
using System.Text;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class TreeSnapshotStoreTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"ui-driver-snapshots-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private static TreeSnapshotKey Key(params string[] options)
    {
        CliArgs cli = new(["tree", .. options]);
        return TreeSnapshotKey.From("sample", cli, TreeOptions.Parse(cli));
    }

    private static UiaNodeSnapshot Tree()
    {
        return new UiaNodeSnapshot("Window", "GalleryWindow", "geoDC \"UI\" Sample", new Rectangle(-8, -8, 1936, 1056),
            true, false, UiaNodeState.None, [],
            [
                new UiaNodeSnapshot("Edit", null, "Name", new Rectangle(10, 20, 80, 24), false, true,
                    new UiaNodeState("line 1\nline 2", Selected: true), ["Value", "SelectionItem"], [], "42,7"),
                new UiaNodeSnapshot("CheckBox", "GridToggle", "", Rectangle.Empty, true, false,
                    new UiaNodeState(Toggled: "mixed", Expanded: "partial", Scroll: "h=n/a v=40%"),
                    ["Toggle", "ExpandCollapse", "Scroll"], [])
            ],
            "42,1");
    }

    private static string Text(IEnumerable<UiaNodeSnapshot> roots)
    {
        StringBuilder builder = new();
        foreach (UiaNodeSnapshot root in roots)
        {
            root.WriteTree(builder);
        }

        return builder.ToString();
    }

    [Test]
    public void WrittenSnapshot_ReadsBackWithEveryField()
    {
        TreeSnapshotStore store = new(_directory);
        TreeSnapshotKey key = Key("--interactable", "--scope", "InputsPage");
        UiaNodeSnapshot tree = Tree();

        string path = store.Write(key, [tree]);
        TreeSnapshotRead read = store.Read(key);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(Path.Combine(_directory, key.FileName)));
            Assert.That(read.Problem, Is.Null);
            Assert.That(read.Roots, Has.Count.EqualTo(1));
            UiaNodeSnapshot root = read.Roots?[0] ?? throw new AssertionException("no root was read");
            Assert.That(Text([root]), Is.EqualTo(Text([tree])));
            Assert.That(root.RuntimeId, Is.EqualTo("42,1"));
            Assert.That(root.Children[0].State, Is.EqualTo(tree.Children[0].State));
            Assert.That(root.Children[0].RuntimeId, Is.EqualTo("42,7"));
            Assert.That(root.Children[1].State, Is.EqualTo(tree.Children[1].State));
            Assert.That(root.Children[1].AutomationId, Is.EqualTo("GridToggle"));
            Assert.That(root.Children[0].AutomationId, Is.Null);
            Assert.That(UiaTreeDiff.Compare([tree], [root], true).IsEmpty, Is.True);
        });
    }

    [Test]
    public void StoredBounds_AreAFourNumberArray()
    {
        TreeSnapshotStore store = new(_directory);

        string json = File.ReadAllText(store.Write(Key(), [Tree()]));

        Assert.That(json, Does.Contain("\"bounds\":[-8,-8,1936,1056]"));
    }

    [Test]
    public void AWriteReplacesTheSnapshotForItsKey()
    {
        TreeSnapshotStore store = new(_directory);
        TreeSnapshotKey key = Key();
        UiaNodeSnapshot replacement = Tree() with { Name = "Renamed", Children = [] };

        store.Write(key, [Tree()]);
        store.Write(key, [replacement]);

        Assert.Multiple(() =>
        {
            Assert.That(store.Read(key).Roots?.Single().Name, Is.EqualTo("Renamed"));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void NoStoredSnapshot_ReadsAsABaselineWithoutAProblem()
    {
        TreeSnapshotRead read = new TreeSnapshotStore(_directory).Read(Key());

        Assert.That(read, Is.EqualTo(new TreeSnapshotRead(null, null)));
    }

    [TestCase("not json")]
    [TestCase("{\"version\":1,\"key\":{\"target\":\"sample\"},\"roots\":null}")]
    [TestCase("{\"version\":99,\"key\":{\"target\":\"sample\"},\"roots\":[]}")]
    public void AnUnusableFile_ReadsAsABaselineWithTheReason(string content)
    {
        TreeSnapshotStore store = new(_directory);
        TreeSnapshotKey key = Key();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PathFor(key), content);

        TreeSnapshotRead read = store.Read(key);

        Assert.Multiple(() =>
        {
            Assert.That(read.Roots, Is.Null);
            Assert.That(read.Problem, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void AFileStoredForOtherArguments_IsNotUsed()
    {
        TreeSnapshotStore store = new(_directory);
        TreeSnapshotKey key = Key("--scope", "InputsPage");
        TreeSnapshotKey other = Key("--scope", "OverlaysPage");
        store.Write(other, [Tree()]);
        File.Move(store.PathFor(other), store.PathFor(key));

        Assert.That(store.Read(key).Problem, Does.Contain("different tree arguments"));
    }

    [Test]
    public void Key_CoversTargetIdScopeWindowMainWindowDepthAndInteractable()
    {
        string[] fileNames =
        [
            Key().FileName,
            TreeSnapshotKey.From("app", new CliArgs(["tree"]), TreeOptions.Parse(new CliArgs(["tree"]))).FileName,
            Key("--id", "GridToggle").FileName,
            Key("--scope", "InputsPage").FileName,
            Key("--window", "title:Popup").FileName,
            Key("--main-window").FileName,
            Key("--depth", "6").FileName,
            Key("--interactable").FileName,
            Key("--interactable", "--depth", "4").FileName
        ];

        Assert.That(fileNames, Is.Unique);
    }

    [Test]
    public void Key_IgnoresDiffBoundsAndTimeout_AndAppliesTheDefaultDepth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Key("--scope", "InputsPage", "--diff", "--bounds", "--timeout", "2"),
                Is.EqualTo(Key("--scope", "InputsPage")));
            Assert.That(Key("--depth", "4"), Is.EqualTo(Key()));
            Assert.That(Key().FileName, Does.Match("^sample-[0-9a-f]{16}\\.json$"));
        });
    }

    [Test]
    public void DefaultStore_LivesUnderDriverSnapshotsInTheWorkingDirectory()
    {
        Assert.That(TreeSnapshotStore.Default().DirectoryPath,
            Is.EqualTo(Path.GetFullPath(Path.Combine(".ui-driver", "snapshots"))));
    }
}