using System.Drawing;
using System.IO;
using System.Text.Json;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class AutomationAuditTests
{
    private static readonly string[] None = [];
    private static readonly string[] Invoke = ["Invoke"];

    private static int _runtimeIds;

    private static UiaNodeSnapshot Node(string controlType, string? id, string[] patterns,
        params UiaNodeSnapshot[] children)
    {
        return Named(controlType, id, id ?? string.Empty, patterns, children);
    }

    private static UiaNodeSnapshot Named(string controlType, string? id, string name, string[] patterns,
        params UiaNodeSnapshot[] children)
    {
        return new UiaNodeSnapshot(controlType, id, name, new Rectangle(0, 0, 10, 10), true, false,
            UiaNodeState.None, patterns, children, $"rt-{Interlocked.Increment(ref _runtimeIds)}");
    }

    private static UiaNodeSnapshot Window(params UiaNodeSnapshot[] children)
    {
        return Node("Window", null, None, children);
    }

    private static IReadOnlyList<AuditFinding> Audit(params UiaNodeSnapshot[] roots)
    {
        return AutomationAudit.Run(roots).Findings;
    }

    private static IReadOnlyList<AuditFinding> Of(IReadOnlyList<AuditFinding> findings, string rule)
    {
        return [.. findings.Where(finding => finding.Rule == rule)];
    }

    [Test]
    public void A_tree_that_follows_the_conventions_has_no_findings()
    {
        UiaNodeSnapshot root = Window(
            Node("TabItem", "LandingTab", ["SelectionItem"]),
            Node("Pane", "SetupPage", None,
                Node("Button", "SaveButton", Invoke),
                Node("Edit", "VesselNameInput", ["Value"]),
                Node("List", "JobList", ["Selection", "Scroll"],
                    Named("ListItem", null, "Stage 2", ["SelectionItem", "ScrollItem"]))));

        Assert.That(Audit(root), Is.Empty);
    }

    [Test]
    public void Missing_id_reports_interactable_nodes_without_an_automation_id()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None,
                Named("Button", null, "Save", Invoke),
                Named("Text", null, "Heading", None),
                Named("ScrollItemOnly", null, "Row", ["ScrollItem", "Selection"])));

        IReadOnlyList<AuditFinding> findings = Audit(root);

        Assert.That(findings, Has.Count.EqualTo(1));
        AuditFinding finding = findings[0];
        Assert.Multiple(() =>
        {
            Assert.That(finding.Rule, Is.EqualTo(AutomationAudit.MissingId));
            Assert.That(finding.Role, Is.EqualTo("Button"));
            Assert.That(finding.Id, Is.Null);
            Assert.That(finding.Name, Is.EqualTo("Save"));
            Assert.That(finding.Path, Is.EqualTo(new[] { "SetupPage" }));
            Assert.That(finding.Scope, Is.EqualTo("SetupPage"));
            Assert.That(finding.Occurrences, Is.EqualTo(1));
            Assert.That(finding.Message, Does.Contain("Invoke"));
        });
    }

    [Test]
    public void Missing_id_exempts_data_bound_items_and_their_content()
    {
        UiaNodeSnapshot root = Window(
            Node("DataGrid", "PointGridList", ["Selection", "Scroll"],
                Named("HeaderItem", null, "Easting", Invoke),
                Named("DataItem", null, "Row 1", ["SelectionItem"],
                    Named("Custom", null, "1.234", ["Invoke", "Value"]))),
            Node("List", "JobList", ["Selection"],
                Named("ListItem", null, "Stage 2", ["SelectionItem"],
                    Named("CheckBox", null, "Include", ["Toggle"]))));

        Assert.That(Of(Audit(root), AutomationAudit.MissingId), Is.Empty);
    }

    [Test]
    public void Framework_chrome_subtrees_are_not_audited()
    {
        UiaNodeSnapshot root = Window(
            Node("TitleBar", "TitleBar", None,
                Named("Button", "Close", "Close", Invoke)),
            Node("Pane", "ContentScrollPanel", ["Scroll"],
                Node("ScrollBar", null, None,
                    Named("Button", null, "Line down", Invoke))),
            Node("ComboBox", "UnitCombo", ["ExpandCollapse"],
                Node("Edit", "PART_EditableTextBox", ["Value"])));

        Assert.That(Audit(root), Is.Empty);
    }

    [TestCase("SaveButton", true)]
    [TestCase("VesselNameInput", true)]
    [TestCase("UnitCombo", true)]
    [TestCase("PointsTab", true)]
    [TestCase("JobList", true)]
    [TestCase("ShowGridToggle", true)]
    [TestCase("StatusBadge", true)]
    [TestCase("SetupPage", true)]
    [TestCase("PopupFooterPanel", true)]
    [TestCase("Step2Button", true)]
    [TestCase("Button", false)]
    [TestCase("saveButton", false)]
    [TestCase("Save_Button", false)]
    [TestCase("SaveBtn", false)]
    [TestCase("CrsPickerEpochBox", false)]
    [TestCase("PersonnelMetric", false)]
    [TestCase("FoundationsNavItem", true)]
    [TestCase("SensorEditMenuItem", true)]
    [TestCase("ShoreControlStatusText", true)]
    [TestCase("ImportSourceChoice", true)]
    [TestCase("ImportCrsPicker", true)]
    [TestCase("AboutPopup", true)]
    [TestCase("Text", false)]
    [TestCase("Rebutton", false)]
    public void Ids_are_well_formed_only_as_pascal_case_noun_and_role(string id, bool wellFormed)
    {
        Assert.That(AutomationConventions.IsWellFormed(id), Is.EqualTo(wellFormed));
    }

    [Test]
    public void Id_naming_reports_each_malformed_id_once_with_its_occurrences()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None,
                Node("Button", "SaveBtn", Invoke),
                Node("Text", "SummaryLabel", None)),
            Node("Pane", "SurveyPage", None,
                Node("Button", "SaveBtn", Invoke)));

        IReadOnlyList<AuditFinding> findings = Of(Audit(root), AutomationAudit.IdNaming);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(finding => finding.Id), Is.EqualTo(new[] { "SaveBtn", "SummaryLabel" }));
            Assert.That(findings[0].Occurrences, Is.EqualTo(2));
            Assert.That(findings[0].Path, Is.EqualTo(new[] { "SetupPage" }),
                "a grouped finding points at the first occurrence");
            Assert.That(findings[1].Occurrences, Is.EqualTo(1));
        });
    }

    [Test]
    public void Duplicate_id_reports_ids_repeated_within_one_page()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None,
                Node("Button", "SaveButton", Invoke),
                Node("Pane", "DetailsPanel", None,
                    Node("Button", "SaveButton", Invoke))));

        IReadOnlyList<AuditFinding> findings = Audit(root);

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(findings[0].Rule, Is.EqualTo(AutomationAudit.DuplicateId));
            Assert.That(findings[0].Id, Is.EqualTo("SaveButton"));
            Assert.That(findings[0].Scope, Is.EqualTo("SetupPage"));
            Assert.That(findings[0].Occurrences, Is.EqualTo(2));
            Assert.That(findings[0].Path, Is.EqualTo(new[] { "SetupPage" }));
        });
    }

    [Test]
    public void Duplicate_id_allows_template_parts_listed_in_the_allowlist()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None,
                Node("Group", "VesselPanel", None, Node("Button", "HelpButton", Invoke)),
                Node("Group", "JobPanel", None, Node("Button", "HelpButton", Invoke)),
                Node("Group", "SourcePanel", None, Node("Button", "CrsPickerResolveButton", Invoke)),
                Node("Group", "TargetPanel", None, Node("Button", "CrsPickerResolveButton", Invoke)),
                Node("Group", "FirstWizardPanel", None, Node("Button", "Step1Button", Invoke)),
                Node("Group", "SecondWizardPanel", None, Node("Button", "Step1Button", Invoke))));

        Assert.Multiple(() =>
        {
            Assert.That(AutomationConventions.IsTemplatePart("HelpButton"), Is.True);
            Assert.That(AutomationConventions.IsTemplatePart("CrsPickerResolveButton"), Is.True);
            Assert.That(AutomationConventions.IsTemplatePart("Step12Button"), Is.True);
            Assert.That(AutomationConventions.IsTemplatePart("SaveButton"), Is.False);
            Assert.That(AutomationConventions.IsTemplatePart("XHelpButton"), Is.False, "globs match the whole id");
            Assert.That(Of(Audit(root), AutomationAudit.DuplicateId), Is.Empty);
        });
    }

    [Test]
    public void Toolkit_template_names_are_exempt_from_the_naming_and_duplicate_rules()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None,
                Node("Group", "VesselPanel", None, Node("Text", "Chrome", None), Node("Button", "HeaderToggle", Invoke)),
                Node("Group", "JobPanel", None, Node("Text", "Chrome", None), Node("Button", "HeaderToggle", Invoke))));

        IReadOnlyList<AuditFinding> findings = Audit(root);

        Assert.Multiple(() =>
        {
            Assert.That(AutomationConventions.IsToolkitElementName("Chrome"), Is.True);
            Assert.That(AutomationConventions.IsToolkitElementName("HeaderToggle"), Is.True);
            Assert.That(AutomationConventions.IsToolkitElementName("PART_Popup"), Is.False,
                "PART_* parts are framework parts, not toolkit names");
            Assert.That(Of(findings, AutomationAudit.IdNaming), Is.Empty);
            Assert.That(Of(findings, AutomationAudit.DuplicateId), Is.Empty);
        });
    }

    [Test]
    public void Duplicate_id_counts_each_page_scope_separately()
    {
        UiaNodeSnapshot root = Window(
            Node("Button", "SaveButton", Invoke),
            Node("Pane", "SetupPage", None, Node("Button", "SaveButton", Invoke)),
            Node("Pane", "SurveyPage", None, Node("Button", "SaveButton", Invoke)),
            Node("Pane", "PopupPanel", None, Node("Button", "SaveButton", Invoke)),
            Node("Window", "ApproveWindow", None, Node("Button", "SaveButton", Invoke)),
            Node("List", "RowList", ["Selection"],
                Named("ListItem", null, "Row 1", ["SelectionItem"], Node("Button", "RemoveButton", Invoke)),
                Named("ListItem", null, "Row 2", ["SelectionItem"], Node("Button", "RemoveButton", Invoke))));

        Assert.That(Of(Audit(root), AutomationAudit.DuplicateId), Is.Empty);
    }

    [Test]
    public void A_page_scope_id_belongs_to_the_scope_it_sits_in()
    {
        UiaNodeSnapshot root = Window(
            Node("Pane", "SetupPage", None, Node("Pane", "SetupPage", None)));

        IReadOnlyList<AuditFinding> findings = Of(Audit(root), AutomationAudit.DuplicateId);

        Assert.That(findings, Is.Empty, "the inner SetupPage sits in the outer page's scope, the outer in the window's");
    }

    [Test]
    public void Duplicate_id_without_a_page_scope_falls_back_to_the_audit_root()
    {
        UiaNodeSnapshot scope = Node("Group", "CommandBarPanel", None,
            Node("Button", "SaveButton", Invoke),
            Node("Group", null, None, Node("Button", "SaveButton", Invoke)));

        IReadOnlyList<AuditFinding> findings = Audit(scope);

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(findings[0].Scope, Is.EqualTo("CommandBarPanel"));
            Assert.That(findings[0].Path, Is.EqualTo(new[] { "CommandBarPanel" }));
            Assert.That(findings[0].Message, Does.Contain("page scope 'CommandBarPanel'"));
        });
    }

    [Test]
    public void A_node_reached_through_two_roots_is_audited_once()
    {
        UiaNodeSnapshot popup = Node("Window", null, None, Named("Button", null, "Approve", Invoke));
        UiaNodeSnapshot main = Window(popup);

        Assert.That(Audit(main, popup), Has.Count.EqualTo(1));
    }

    [Test]
    public void Reserved_id_is_allowed_once_directly_in_a_top_level_window()
    {
        UiaNodeSnapshot root = Window(
            Node("Group", null, None, Node("TabItem", "SetupTab", ["SelectionItem"])),
            Node("Pane", "CalibrationPage", None, Node("TabItem", "SetupTab", ["SelectionItem"])),
            Node("Pane", "PopupPanel", None, Node("TabItem", "GnssTab", ["SelectionItem"])));

        IReadOnlyList<AuditFinding> findings = Of(Audit(root), AutomationAudit.ReservedId);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(finding => (finding.Id, finding.Scope)),
                Is.EqualTo(new[] { ("SetupTab", "CalibrationPage"), ("GnssTab", "PopupPanel") }));
            Assert.That(findings[0].Message, Does.Contain("reserved"));
        });
    }

    [Test]
    public void Reserved_id_is_reported_on_a_second_window_level_occurrence_and_under_a_scoped_root()
    {
        UiaNodeSnapshot window = Window(
            Node("TabItem", "ReportsTab", ["SelectionItem"]),
            Node("TabItem", "ReportsTab", ["SelectionItem"]));
        UiaNodeSnapshot scoped = Node("Group", "SettingsPanel", None, Node("TabItem", "LandingTab", ["SelectionItem"]));

        Assert.Multiple(() =>
        {
            Assert.That(Of(Audit(window), AutomationAudit.ReservedId), Has.Count.EqualTo(1));
            Assert.That(Of(Audit(scoped), AutomationAudit.ReservedId), Has.Count.EqualTo(1));
        });
    }

    [TestCase("JobItemViewModel { Name = Stage 2, Id = 01J }", "JobItemViewModel", true)]
    [TestCase("EmptyRecord { }", "EmptyRecord", true)]
    [TestCase("geoDC.App.ViewModels.JobItemViewModel", "geoDC.App.ViewModels.JobItemViewModel", false)]
    [TestCase("Stage 2", null, false)]
    [TestCase("Set { x } aside", null, false)]
    [TestCase("e.g. this", null, false)]
    [TestCase("1.2.3", null, false)]
    public void Raw_type_names_are_recognised(string name, string? type, bool record)
    {
        (string Type, bool Record)? raw = AutomationAudit.RawTypeName(name);

        Assert.Multiple(() =>
        {
            Assert.That(raw?.Type, Is.EqualTo(type));
            if (raw is { } found)
            {
                Assert.That(found.Record, Is.EqualTo(record));
            }
        });
    }

    [Test]
    public void Raw_name_reports_each_type_once_with_its_occurrences()
    {
        UiaNodeSnapshot root = Window(
            Node("List", "JobList", ["Selection"],
                Named("ListItem", null, "JobRow { Name = A }", ["SelectionItem"]),
                Named("ListItem", null, "JobRow { Name = B }", ["SelectionItem"]),
                Named("ListItem", null, "geoDC.App.ViewModels.SetRow", ["SelectionItem"])));

        IReadOnlyList<AuditFinding> findings = Of(Audit(root), AutomationAudit.RawName);

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(finding => finding.Name),
                Is.EqualTo(new[] { "JobRow { Name = A }", "geoDC.App.ViewModels.SetRow" }));
            Assert.That(findings[0].Occurrences, Is.EqualTo(2));
            Assert.That(findings[0].Role, Is.EqualTo("ListItem"));
            Assert.That(findings[0].Path, Is.EqualTo(new[] { "JobList" }));
            Assert.That(findings[0].Message, Does.Contain("ToString"));
        });
    }

    [Test]
    public void Findings_are_ordered_by_rule_then_document_order_and_are_stable()
    {
        UiaNodeSnapshot root = Window(
            Named("ListItem", null, "Row { A = 1 }", ["SelectionItem"]),
            Node("Pane", "CalibrationPage", None,
                Node("TabItem", "GnssTab", ["SelectionItem"]),
                Node("Button", "zButton", Invoke),
                Node("Button", "OkButton", Invoke),
                Node("Button", "OkButton", Invoke),
                Named("Button", null, "Second", Invoke)),
            Named("Button", null, "First", Invoke));

        IReadOnlyList<AuditFinding> first = Audit(root);
        IReadOnlyList<AuditFinding> second = Audit(root);

        Assert.Multiple(() =>
        {
            Assert.That(first.Select(finding => finding.Rule), Is.EqualTo(new[]
            {
                AutomationAudit.MissingId, AutomationAudit.MissingId, AutomationAudit.IdNaming,
                AutomationAudit.DuplicateId, AutomationAudit.ReservedId, AutomationAudit.RawName
            }));
            Assert.That(first[0].Name, Is.EqualTo("Second"));
            Assert.That(first[1].Name, Is.EqualTo("First"));
            Assert.That(second, Is.EqualTo(first).Using<AuditFinding>(SameFinding));
        });
    }

    [Test]
    public void A_report_without_findings_has_no_failure_and_counts_every_rule()
    {
        AuditReport report = AutomationAudit.Run([Window(Node("Button", "SaveButton", Invoke))]);

        Assert.Multiple(() =>
        {
            Assert.That(report.ToFailure(null, 2), Is.Null);
            Assert.That(report.CountByRule().Keys, Is.EqualTo(AutomationAudit.Rules));
            Assert.That(report.CountByRule().Values, Is.All.EqualTo(0));
        });
    }

    [Test]
    [NonParallelizable]
    public void A_report_with_findings_fails_with_the_findings_exit_code_and_the_findings_on_the_error()
    {
        AuditReport report = AutomationAudit.Run([Window(Named("Button", null, "Save", Invoke))]);

        DriverException? failure = report.ToFailure("SetupPage", 7);

        Assert.That(failure, Is.Not.Null);
        if (failure is null)
        {
            return;
        }

        JsonElement error = CaptureFailureError(failure);
        Assert.Multiple(() =>
        {
            Assert.That(failure.Code, Is.EqualTo("audit-findings"));
            Assert.That(failure.ExitCode, Is.EqualTo(AutomationAudit.FindingsExitCode));
            Assert.That(failure.ExitCode, Is.Not.EqualTo(0));
            Assert.That(failure.Message, Does.Contain("1 missing-id"));
            Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("audit-findings"));
            Assert.That(error.GetProperty("scope").GetString(), Is.EqualTo("SetupPage"));
            Assert.That(error.GetProperty("walked").GetInt32(), Is.EqualTo(7));
            Assert.That(error.GetProperty("count").GetInt32(), Is.EqualTo(1));
            Assert.That(error.GetProperty("byRule").GetProperty("missing-id").GetInt32(), Is.EqualTo(1));
            JsonElement finding = error.GetProperty("findings")[0];
            Assert.That(finding.GetProperty("rule").GetString(), Is.EqualTo("missing-id"));
            Assert.That(finding.GetProperty("role").GetString(), Is.EqualTo("Button"));
            Assert.That(finding.GetProperty("id").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(finding.GetProperty("path").GetArrayLength(), Is.EqualTo(0));
            Assert.That(finding.GetProperty("occurrences").GetInt32(), Is.EqualTo(1));
        });
    }

    private static bool SameFinding(AuditFinding left, AuditFinding right)
    {
        return left.Rule == right.Rule && left.Id == right.Id && left.Name == right.Name &&
               left.Path.SequenceEqual(right.Path) && left.Scope == right.Scope &&
               left.Occurrences == right.Occurrences && left.Message == right.Message;
    }

    private static JsonElement CaptureFailureError(DriverException failure)
    {
        TextWriter original = Console.Out;
        using StringWriter captured = new();
        Console.SetOut(captured);
        try
        {
            JsonOutput.Failure("audit", failure.Code, failure.Message, failure.Candidates, null, failure.State,
                failure.Lookup, failure.Details);
        }
        finally
        {
            Console.SetOut(original);
        }

        using JsonDocument document = JsonDocument.Parse(captured.ToString());
        return document.RootElement.GetProperty("error").Clone();
    }
}