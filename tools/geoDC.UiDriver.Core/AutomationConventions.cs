using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace geoDC.UiDriver;

/// <summary>
///     The geoDC AutomationId conventions that <c>audit</c> checks, and the allowlists it applies. The UI driver keeps
///     them only here. <see cref="Roles" /> is the closed list of AutomationId roles.
/// </summary>
/// <remarks>
///     <para>
///         Ids may name a glob: <c>*</c> matches any run of characters, so <c>CrsPicker*</c> covers every part of the
///         CRS picker's templates. Every other character matches itself, case-sensitively.
///     </para>
///     <para>
///         <see cref="TemplatePartIds" /> and <see cref="ReservedShellIds" /> mirror declarations in
///         geoDC's <c>src/geoDC.App.UI</c> and <c>src/geoDC.App/MainWindow.xaml</c>. The UI driver tests fail when an
///         entry does not match anything declared there.
///     </para>
/// </remarks>
public static class AutomationConventions
{
    /// <summary>
    ///     The closed AutomationId role vocabulary. A well-formed id is PascalCase <c>{Noun}{Role}</c> ending in one of
    ///     these, with a non-empty noun.
    /// </summary>
    public static readonly IReadOnlyList<string> Roles =
        ["Button", "Input", "Combo", "Picker", "Choice", "Toggle", "Tab", "List", "Item", "Text", "Badge", "Page", "Panel",
            "Popup"];

    /// <summary>
    ///     The eight shell destinations, keyed on <c>UiNavBarItem</c> in <c>MainWindow.xaml</c>. They are reserved
    ///     application-wide: shell chrome is present on every page, so a page that reuses one is ambiguous however it
    ///     is scoped.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedShellIds =
    [
        "LandingTab", "SetupTab", "SurveyTab", "CalibrationTab", "VerificationTab", "ShoreControlTab", "GnssTab",
        "ReportsTab"
    ];

    /// <summary>
    ///     Fixed template-part ids of <c>geoDC.App.UI</c> controls. Each control instance has its own copy, so these
    ///     ids repeat within a page and a script tells them apart with <c>--scope &lt;host&gt;</c>. <c>audit</c> does
    ///     not report them as duplicates, but still checks them against the naming rule.
    /// </summary>
    /// <remarks>
    ///     The list covers the parts documented in geoDC's <c>docs/ui/controls.md</c>, <c>docs/ui/layout.md</c> and
    ///     <c>docs/ui/popups.md</c>: the help badge, convention token and C-O tag, coordinate, date and CRS pickers,
    ///     calendar, list view, overlay drawer, wizard frame and stepper, and the popup footer. Because the match is
    ///     by id alone, a page that declares one of these ids itself, outside a template, is not reported as a
    ///     duplicate either. Template elements that carry only an <c>x:Name</c> are covered by
    ///     <see cref="ToolkitElementNames" /> instead.
    /// </remarks>
    public static readonly IReadOnlyList<string> TemplatePartIds =
    [
        "HelpButton",
        "NoteActionButton", "ConventionTokenButton",
        "AddItemButton",
        "CoordinatePointOrdinate1Input", "CoordinatePointOrdinate2Input", "CoordinatePointOrdinate3Input",
        "CoordinatePointValidationText", "CoordinateDisplayCopyButton",
        "DatePickerUtcText", "DatePickerCalendarToggle", "DatePickerCalendarPanel",
        "CalendarTimeInput", "CalendarDoneButton",
        "CrsPicker*",
        "OverlayDrawerPanel", "OverlayDrawerCloseButton",
        "WizardStepperList", "WizardContentPanel", "WizardStatusText", "CancelButton", "BackButton", "AdvanceButton",
        "Step*Button",
        "PopupFooterPanel"
    ];

    /// <summary>
    ///     The ids of the popup frames that <c>geoDC.App.UI</c> assigns by default (<c>PopupPanel</c>, the dialog frame
    ///     and the loading popup). Each frame is a page scope of its own, so its footer action ids may repeat ids on the
    ///     page underneath. A popup that declares its own <c>PopupPresentation.AutomationId</c> is not listed here and
    ///     shares the scope of the page or window it is shown over, unless it is a window of its own.
    /// </summary>
    public static readonly IReadOnlyList<string> PopupFrameIds = ["PopupPanel", "DialogPanel", "LoadingPanel"];

    /// <summary>
    ///     The control types of data-bound items. Items are addressed by UIA Name through <c>select</c>, so an item and
    ///     everything inside it are not reported for a missing AutomationId. Each item is a page scope of its own, so
    ///     ids from its item template may repeat across items.
    /// </summary>
    public static readonly IReadOnlyList<string> ItemControlTypes = ["ListItem", "DataItem", "TreeItem", "HeaderItem"];

    /// <summary>
    ///     Control types of framework and window chrome that no geoDC view can give an AutomationId: scroll bars (their
    ///     line and page buttons) and the non-client title bar with its system menu and caption buttons. <c>audit</c>
    ///     skips them and everything below them.
    /// </summary>
    public static readonly IReadOnlyList<string> FrameworkChromeControlTypes = ["ScrollBar", "TitleBar"];

    /// <summary>
    ///     AutomationIds that WPF's own templates assign (<c>PART_EditableTextBox</c> in an editable combo box, for
    ///     example). They are exempt from the naming and duplicate rules.
    /// </summary>
    public static readonly IReadOnlyList<string> FrameworkPartIds = ["PART_*"];

    /// <summary>The logical-name prefix of the toolkit XAML embedded by <c>ToolkitXaml.props</c>.</summary>
    public const string ToolkitXamlResourcePrefix = "ToolkitXaml/";

    private static readonly Regex XamlElementName =
        new("\\bx:Name=\"(?<name>[^\"{]+)\"", RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlySet<string>> EmbeddedToolkitElementNames = new(ReadToolkitElementNames);

    private static readonly Regex PascalCase = new("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

    private static readonly Regex TemplatePartPattern = GlobUnion(TemplatePartIds);

    private static readonly Regex FrameworkPartPattern = GlobUnion(FrameworkPartIds);

    /// <summary>
    ///     True when <paramref name="automationId" /> is PascalCase (an upper-case letter, then only letters and digits)
    ///     and ends in a role from <see cref="Roles" /> with at least one character of noun before it.
    /// </summary>
    public static bool IsWellFormed(string automationId)
    {
        return PascalCase.IsMatch(automationId) &&
               Roles.Any(role => automationId.Length > role.Length &&
                                 automationId.EndsWith(role, StringComparison.Ordinal));
    }

    /// <summary>True when <paramref name="automationId" /> is one of <see cref="TemplatePartIds" />.</summary>
    public static bool IsTemplatePart(string automationId)
    {
        return TemplatePartPattern.IsMatch(automationId);
    }

    /// <summary>
    ///     Every <c>x:Name</c> declared in <c>src/geoDC.App.UI</c> XAML, other than WPF's <c>PART_*</c> parts, read
    ///     from the copies the build embeds in the UI driver. WPF reports an element's <c>x:Name</c> as its AutomationId
    ///     when none is set, so these ids appear on every templated toolkit control. They name template internals, not
    ///     elements a script is meant to address, so <c>audit</c> checks them against neither the naming nor the
    ///     duplicate rule. The match is by id alone, so an app element whose AutomationId equals one of these names is
    ///     exempt too. Empty when the UI driver was built without the embedded XAML.
    /// </summary>
    public static IReadOnlySet<string> ToolkitElementNames => EmbeddedToolkitElementNames.Value;

    /// <summary>The <c>x:Name</c> values declared in <paramref name="xaml" />, other than <c>PART_*</c> parts.</summary>
    public static IEnumerable<string> ElementNamesIn(string xaml)
    {
        return XamlElementName.Matches(xaml)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !IsFrameworkPart(name));
    }

    /// <summary>True when <paramref name="automationId" /> is one of <see cref="ToolkitElementNames" />.</summary>
    public static bool IsToolkitElementName(string automationId)
    {
        return ToolkitElementNames.Contains(automationId);
    }

    /// <summary>True when <paramref name="automationId" /> is one of <see cref="FrameworkPartIds" />.</summary>
    public static bool IsFrameworkPart(string automationId)
    {
        return FrameworkPartPattern.IsMatch(automationId);
    }

    /// <summary>True when <paramref name="automationId" /> is one of <see cref="ReservedShellIds" />.</summary>
    public static bool IsReservedShellId(string automationId)
    {
        return ReservedShellIds.Contains(automationId, StringComparer.Ordinal);
    }

    /// <summary>True when <paramref name="controlType" /> is one of <see cref="ItemControlTypes" />.</summary>
    public static bool IsItem(string controlType)
    {
        return ItemControlTypes.Contains(controlType, StringComparer.Ordinal);
    }

    /// <summary>True when <paramref name="controlType" /> is one of <see cref="FrameworkChromeControlTypes" />.</summary>
    public static bool IsFrameworkChrome(string controlType)
    {
        return FrameworkChromeControlTypes.Contains(controlType, StringComparer.Ordinal);
    }

    /// <summary>
    ///     True when <paramref name="node" /> starts a page scope for its descendants: an id ending in <c>Page</c> with a
    ///     noun before it, a popup frame from <see cref="PopupFrameIds" />, a <c>Window</c> (an owned popup window
    ///     appears under its owner), or a data-bound item.
    /// </summary>
    public static bool StartsPageScope(UiaNodeSnapshot node)
    {
        if (node.AutomationId is { Length: > 4 } id && id.EndsWith("Page", StringComparison.Ordinal))
        {
            return true;
        }

        return (node.AutomationId is { } frameId && PopupFrameIds.Contains(frameId, StringComparer.Ordinal)) ||
               node.ControlType == "Window" || IsItem(node.ControlType);
    }

    /// <summary>A regex matching any of <paramref name="globs" /> in full, where <c>*</c> matches any run of characters.</summary>
    public static Regex GlobUnion(IEnumerable<string> globs)
    {
        IEnumerable<string> alternatives =
            globs.Select(glob => string.Join(".*", glob.Split('*').Select(Regex.Escape)));
        return new Regex($"^(?:{string.Join("|", alternatives)})$", RegexOptions.CultureInvariant);
    }

    private static IReadOnlySet<string> ReadToolkitElementNames()
    {
        Assembly assembly = typeof(AutomationConventions).Assembly;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ToolkitXamlResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream? stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            using StreamReader reader = new(stream);
            names.UnionWith(ElementNamesIn(reader.ReadToEnd()));
        }

        return names;
    }
}