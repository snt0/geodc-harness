using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     A UI driver launch must not open geoDC behind a lock prompt: a lock left by an instance that has gone is cleared,
///     and any other lock is reported rather than launched into.
/// </summary>
[TestFixture]
public sealed class ProjectLockFileTests
{
    private const string ThisMachine = "DRIVER-HOST";

    private string _directory = string.Empty;
    private string _project = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "geodc-ui-driver-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _project = Path.Combine(_directory, "sample.gdcproj");
        File.WriteAllText(_project, "package");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void No_lock_means_nothing_to_clear()
    {
        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.None));
            Assert.That(found.BlocksLaunch, Is.False);
        });
    }

    [Test]
    public void A_lock_from_a_process_that_has_gone_is_cleared()
    {
        WriteLock(ThisMachine, 4242);

        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.Stale));
            Assert.That(found.BlocksLaunch, Is.False);
            Assert.That(File.Exists(ProjectLockFile.PathFor(_project)), Is.False);
        });
    }

    [Test]
    public void A_lock_from_a_running_process_is_kept_and_blocks_the_launch()
    {
        WriteLock(ThisMachine.ToLowerInvariant(), 4242);

        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, pid => pid == 4242);

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.Live));
            Assert.That(found.BlocksLaunch, Is.True);
            Assert.That(found.Describe(), Does.Contain("4242"));
            Assert.That(File.Exists(ProjectLockFile.PathFor(_project)), Is.True);
        });
    }

    [Test]
    public void A_lock_from_another_computer_is_kept_and_blocks_the_launch()
    {
        WriteLock("OTHER-HOST", 4242);

        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.OtherComputer));
            Assert.That(found.BlocksLaunch, Is.True);
            Assert.That(File.Exists(ProjectLockFile.PathFor(_project)), Is.True);
        });
    }

    [TestCase("not json")]
    [TestCase("{\"userName\": \"surveyor\"}")]
    [TestCase("{\"machineName\": \"DRIVER-HOST\", \"processId\": 4242}")]
    public void A_lock_that_names_no_holder_is_kept_and_blocks_the_launch(string content)
    {
        File.WriteAllText(ProjectLockFile.PathFor(_project), content);

        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.Unreadable));
            Assert.That(found.BlocksLaunch, Is.True);
            Assert.That(File.Exists(ProjectLockFile.PathFor(_project)), Is.True);
        });
    }

    [Test]
    public void A_lock_replaced_while_it_is_being_cleared_is_put_back_and_blocks_the_launch()
    {
        WriteLock(ThisMachine, 4242);
        string lockPath = ProjectLockFile.PathFor(_project);

        // The holder check runs after the lock was read: another session takes the lock at that moment.
        ProjectLock found = ProjectLockFile.ClearIfStale(_project, ThisMachine, _ =>
        {
            WriteLock(ThisMachine, 5151);
            return false;
        });

        Assert.Multiple(() =>
        {
            Assert.That(found.Status, Is.EqualTo(ProjectLockStatus.Contended));
            Assert.That(found.BlocksLaunch, Is.True);
            Assert.That(File.ReadAllText(lockPath), Does.Contain("5151"), "the new session keeps its lock");
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        });
    }

    /// <summary>Writes a lock in the shape geoDC's sibling-file lock service writes it.</summary>
    private void WriteLock(string machineName, int processId)
    {
        File.WriteAllText(ProjectLockFile.PathFor(_project),
            $$"""
              {
                "userName": "surveyor",
                "machineName": "{{machineName}}",
                "processId": {{processId}},
                "acquiredAtUtc": "2026-10-16T08:00:00Z",
                "leaseToken": "0123456789abcdef0123456789abcdef"
              }
              """);
    }
}