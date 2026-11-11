using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

/// <summary>One convention breach <c>audit</c> found.</summary>
/// <param name="Rule">The rule's id, one of <see cref="AutomationAudit.Rules" />.</param>
/// <param name="Role">The UIA control type of the node the finding points at.</param>
/// <param name="Id">That node's AutomationId, or null when it has none.</param>
/// <param name="Name">That node's UIA Name; empty when it has none.</param>
/// <param name="Path">
///     The AutomationIds of the node's ancestors from the audit root down, skipping ancestors without one, as
///     <c>find</c> prints them.
/// </param>
/// <param name="Scope">
///     The AutomationId of the page scope the node belongs to (see <see cref="AutomationAudit" />), or null when that
///     scope has none, such as an id-less window or a data-bound item.
/// </param>
/// <param name="Occurrences">
///     How many nodes the finding stands for: the nodes sharing the duplicated id in its scope, the nodes carrying a
///     malformed id or the same raw type name, or 1.
/// </param>
/// <param name="Message">A one-line description of the breach and what fixes it.</param>
public sealed record AuditFinding(
    string Rule,
    string Role,
    string? Id,
    string Name,
    IReadOnlyList<string> Path,
    string? Scope,
    int Occurrences,
    string Message);

/// <summary>What <c>audit</c> found under its roots, in report order.</summary>
public sealed record AuditReport(IReadOnlyList<AuditFinding> Findings)
{
    /// <summary>The number of findings per rule, for every rule in <see cref="AutomationAudit.Rules" /> order.</summary>
    public IReadOnlyDictionary<string, int> CountByRule()
    {
        Dictionary<string, int> counts = [];
        foreach (string rule in AutomationAudit.Rules)
        {
            counts[rule] = Findings.Count(finding => finding.Rule == rule);
        }

        return counts;
    }

    /// <summary>
    ///     The failure <c>audit</c> raises when there are findings: code <c>audit-findings</c>, exit code
    ///     <see cref="AutomationAudit.FindingsExitCode" />, with <c>scope</c>, <c>walked</c>, <c>count</c>,
    ///     <c>byRule</c> and <c>findings</c> added to <c>error</c>. Null when there are none.
    /// </summary>
    public DriverException? ToFailure(string? scope, int walked)
    {
        if (Findings.Count == 0)
        {
            return null;
        }

        string breakdown = string.Join(", ",
            CountByRule().Where(entry => entry.Value > 0).Select(entry => $"{entry.Value} {entry.Key}"));
        return new DriverException("audit-findings",
            $"{Findings.Count} automation convention finding(s): {breakdown}.", AutomationAudit.FindingsExitCode,
            details: Summary(scope, walked));
    }

    /// <summary>The <c>data</c> (or, with findings, the extra <c>error</c> fields) of an <c>audit</c> line.</summary>
    public object Summary(string? scope, int walked)
    {
        return new { scope, walked, count = Findings.Count, byRule = CountByRule(), findings = Findings };
    }
}

/// <summary>
///     Checks walked UIA snapshots against the AutomationId conventions in <see cref="AutomationConventions" />.
/// </summary>
/// <remarks>
///     <para>The rules, in the order findings are reported:</para>
///     <list type="bullet">
///         <item>
///             <c>missing-id</c>: an interactable node (see <see cref="UiaNodeSnapshot.InteractablePatterns" />) with no
///             AutomationId. Data-bound items and everything inside them are exempt, because they are addressed by
///             Name.
///         </item>
///         <item>
///             <c>id-naming</c>: an AutomationId that is not PascalCase <c>{Noun}{Role}</c> with a role from
///             <see cref="AutomationConventions.Roles" />. One finding per distinct id. Toolkit template
///             <c>x:Name</c>s (<see cref="AutomationConventions.ToolkitElementNames" />) and WPF's own <c>PART_*</c>
///             parts are exempt from this rule and the next.
///         </item>
///         <item>
///             <c>duplicate-id</c>: an AutomationId carried by more than one node in the same page scope, other than a
///             template part from <see cref="AutomationConventions.TemplatePartIds" />. One finding per id and scope.
///         </item>
///         <item>
///             <c>reserved-id</c>: a reserved shell id from <see cref="AutomationConventions.ReservedShellIds" /> anywhere
///             but directly in a top-level window, outside every page scope; a second such occurrence is reported too.
///         </item>
///         <item>
///             <c>raw-name</c>: a UIA Name that is unformatted <c>ToString</c> output, either record-style
///             (<c>Foo { Bar = 1 }</c>) or a namespace-qualified type name (<c>geoDC.App.FooViewModel</c>). One finding
///             per distinct type name.
///         </item>
///     </list>
///     <para>
///         A page scope is the nearest ancestor that <see cref="AutomationConventions.StartsPageScope" /> accepts (an id
///         ending in <c>Page</c>, a popup frame, a window, or a data-bound item), else the audit root. A node's own id
///         belongs to the scope it sits in, not to the scope it starts. Within a rule, findings follow the document
///         order of the node each one is anchored on (the first occurrence for a grouped finding), so the same tree
///         always yields the same list. A node reached through two roots (same runtime id) is audited once, and the
///         walk skips <see cref="AutomationConventions.FrameworkChromeControlTypes" /> subtrees.
///     </para>
/// </remarks>
public static class AutomationAudit
{
    /// <summary>Rule id for an interactable node without an AutomationId.</summary>
    public const string MissingId = "missing-id";

    /// <summary>Rule id for an AutomationId that is not PascalCase <c>{Noun}{Role}</c> with a known role.</summary>
    public const string IdNaming = "id-naming";

    /// <summary>Rule id for an AutomationId carried by more than one node in the same page scope.</summary>
    public const string DuplicateId = "duplicate-id";

    /// <summary>Rule id for a reserved shell id used anywhere other than directly in a top-level window.</summary>
    public const string ReservedId = "reserved-id";

    /// <summary>Rule id for a UIA Name that is unformatted <c>ToString</c> output.</summary>
    public const string RawName = "raw-name";

    /// <summary>The process exit code of an <c>audit</c> with findings, distinct from the UI driver's error exit codes.</summary>
    public const int FindingsExitCode = 4;

    /// <summary>Every rule id, in report order.</summary>
    public static readonly IReadOnlyList<string> Rules = [MissingId, IdNaming, DuplicateId, ReservedId, RawName];

    private static readonly Regex RecordName = new(
        @"^(?<type>[A-Za-z_][A-Za-z0-9_.]*) \{ (?:[A-Za-z_][A-Za-z0-9_]* = .* )?\}$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex QualifiedTypeName = new(
        @"^(?<type>(?:[A-Za-z_][A-Za-z0-9_]*\.){2,}[A-Z][A-Za-z0-9_]*(?:`\d+(?:\[.*\])?)?)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Audits the subtrees under <paramref name="roots" /> (roots included); see the remarks for the rules.</summary>
    public static AuditReport Run(IEnumerable<UiaNodeSnapshot> roots)
    {
        Walk walk = new();
        foreach (UiaNodeSnapshot root in roots)
        {
            walk.VisitRoot(root);
        }

        return new AuditReport(walk.Report());
    }

    /// <summary>
    ///     The type name behind a raw <c>ToString</c> UIA Name and whether it is record-style, or null when
    ///     <paramref name="name" /> does not look like one.
    /// </summary>
    public static (string Type, bool Record)? RawTypeName(string name)
    {
        Match record = RecordName.Match(name);
        if (record.Success)
        {
            return (record.Groups["type"].Value, true);
        }

        Match qualified = QualifiedTypeName.Match(name);
        return qualified.Success ? (qualified.Groups["type"].Value, false) : null;
    }

    private static int RuleOrder(string rule)
    {
        for (int index = 0; index < Rules.Count; index++)
        {
            if (Rules[index] == rule)
            {
                return index;
            }
        }

        return Rules.Count;
    }

    private sealed record PageScope(int Key, string? Id, string Role, bool IsTopLevelWindow)
    {
        public string Describe()
        {
            return Id is not null ? $"page scope '{Id}'" : $"the {Role} scope";
        }
    }

    private sealed record Visited(int Index, UiaNodeSnapshot Node, IReadOnlyList<string> Path, PageScope Scope);

    private sealed record Anchored(int Index, AuditFinding Finding);

    private sealed class Walk
    {
        private readonly List<Anchored> _findings = [];
        private readonly Dictionary<string, List<Visited>> _malformed = new(StringComparer.Ordinal);
        private readonly Dictionary<(int Scope, string Id), List<Visited>> _byScopeAndId = [];
        private readonly Dictionary<string, (List<Visited> Nodes, bool Record)> _rawNames =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _allowedReserved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenRuntimeIds = new(StringComparer.Ordinal);
        private readonly List<string> _path = [];
        private int _nextIndex;
        private int _nextScopeKey;

        public void VisitRoot(UiaNodeSnapshot root)
        {
            PageScope scope = new(_nextScopeKey++, root.AutomationId, root.ControlType, root.ControlType == "Window");
            Visit(root, scope, false, true);
        }

        public IReadOnlyList<AuditFinding> Report()
        {
            foreach ((string id, List<Visited> nodes) in _malformed)
            {
                Visited first = nodes[0];
                AddFinding(first, IdNaming, nodes.Count,
                    $"'{id}' is not PascalCase {{Noun}}{{Role}} with a role from " +
                    $"{string.Join(", ", AutomationConventions.Roles)}.");
            }

            foreach (((int _, string id), List<Visited> nodes) in _byScopeAndId)
            {
                if (nodes.Count < 2)
                {
                    continue;
                }

                Visited first = nodes[0];
                AddFinding(first, DuplicateId, nodes.Count,
                    $"'{id}' appears {nodes.Count} times in {first.Scope.Describe()}; ids are unique within a page " +
                    "unless they are template parts.");
            }

            foreach ((string type, (List<Visited> nodes, bool record)) in _rawNames)
            {
                Visited first = nodes[0];
                string form = record ? "record-style ToString output" : "a type name from the default ToString";
                AddFinding(first, RawName, nodes.Count,
                    $"UIA Name \"{first.Node.Name}\" is {form}; give {type} a ToString override that reads as a label.");
            }

            return
            [
                .. _findings
                    .OrderBy(anchored => RuleOrder(anchored.Finding.Rule))
                    .ThenBy(anchored => anchored.Index)
                    .Select(anchored => anchored.Finding)
            ];
        }

        private void Visit(UiaNodeSnapshot node, PageScope scope, bool insideItem, bool isRoot)
        {
            if (AutomationConventions.IsFrameworkChrome(node.ControlType) ||
                (node.RuntimeId is { Length: > 0 } runtimeId && !_seenRuntimeIds.Add(runtimeId)))
            {
                return;
            }

            Visited visited = new(_nextIndex++, node, [.. _path], scope);
            bool isItem = AutomationConventions.IsItem(node.ControlType);
            if (node.IsInteractable && !node.HasAutomationId && !insideItem && !isItem)
            {
                AddFinding(visited, MissingId, 1,
                    $"Interactable {node.ControlType} ({string.Join("/", node.Patterns)}) has no AutomationId.");
            }

            if (node.AutomationId is { Length: > 0 } id && !AutomationConventions.IsFrameworkPart(id) &&
                !AutomationConventions.IsToolkitElementName(id))
            {
                CheckId(visited, id);
            }

            if (node.Name.Length > 0 && RawTypeName(node.Name) is { } raw)
            {
                if (!_rawNames.TryGetValue(raw.Type, out (List<Visited> Nodes, bool Record) entry))
                {
                    entry = ([], raw.Record);
                    _rawNames[raw.Type] = entry;
                }

                entry.Nodes.Add(visited);
            }

            PageScope childScope = !isRoot && AutomationConventions.StartsPageScope(node)
                ? new PageScope(_nextScopeKey++, node.AutomationId, node.ControlType, false)
                : scope;
            bool pushed = false;
            if (node.HasAutomationId && node.AutomationId is { } pathId)
            {
                _path.Add(pathId);
                pushed = true;
            }

            foreach (UiaNodeSnapshot child in node.Children)
            {
                Visit(child, childScope, insideItem || isItem, false);
            }

            if (pushed)
            {
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void CheckId(Visited visited, string id)
        {
            if (!AutomationConventions.IsWellFormed(id))
            {
                if (!_malformed.TryGetValue(id, out List<Visited>? nodes))
                {
                    nodes = [];
                    _malformed[id] = nodes;
                }

                nodes.Add(visited);
            }

            if (!AutomationConventions.IsTemplatePart(id))
            {
                (int, string) key = (visited.Scope.Key, id);
                if (!_byScopeAndId.TryGetValue(key, out List<Visited>? nodes))
                {
                    nodes = [];
                    _byScopeAndId[key] = nodes;
                }

                nodes.Add(visited);
            }

            if (AutomationConventions.IsReservedShellId(id) &&
                !(visited.Scope.IsTopLevelWindow && _allowedReserved.Add(id)))
            {
                AddFinding(visited, ReservedId, 1,
                    $"'{id}' is reserved for the shell navigation bar; name page-level tabs for their content.");
            }
        }

        private void AddFinding(Visited visited, string rule, int occurrences, string message)
        {
            UiaNodeSnapshot node = visited.Node;
            _findings.Add(new Anchored(visited.Index,
                new AuditFinding(rule, node.ControlType, node.AutomationId, node.Name, visited.Path, visited.Scope.Id,
                    occurrences, message)));
        }
    }
}