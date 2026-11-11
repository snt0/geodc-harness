using System.IO;
using System.Text.RegularExpressions;
using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>
///     Keeps the UI driver's convention lists in step with the sources they mirror, so <c>audit</c> does not allow an id
///     the toolkit no longer declares or miss a shell destination.
/// </summary>
[TestFixture]
public sealed class AutomationConventionsTests
{
    private static readonly Regex NavBarItemKey = new(
        "<ui:UiNavBarItem\\b[^>]*?\\sKey=\"([^\"{]+)\"", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Test]
    public void Reserved_shell_ids_are_the_nav_bar_keys_in_the_main_window()
    {
        string mainWindow = File.ReadAllText(Path.Combine(TestPaths.GeoDCRoot, "src", "geoDC.App", "MainWindow.xaml"));
        string[] keys = [.. NavBarItemKey.Matches(mainWindow).Select(match => match.Groups[1].Value)];

        Assert.That(AutomationConventions.ReservedShellIds, Is.EquivalentTo(keys));
    }

    [Test]
    public void Every_template_part_and_popup_frame_id_is_declared_in_the_toolkit()
    {
        string toolkit = ToolkitSource();

        Assert.Multiple(() =>
        {
            foreach (string id in AutomationConventions.TemplatePartIds.Concat(AutomationConventions.PopupFrameIds))
            {
                Assert.That(DeclaredPattern(id).IsMatch(toolkit), Is.True,
                    $"'{id}' is listed in AutomationConventions but nothing in src/geoDC.App.UI declares it.");
            }
        });
    }

    [Test]
    public void Toolkit_element_names_are_the_x_names_declared_in_the_toolkit_xaml()
    {
        string root = Path.Combine(TestPaths.GeoDCRoot, "src", "geoDC.App.UI");
        string[] declared =
        [
            .. ToolkitFiles(root, ".xaml")
                .SelectMany(path => AutomationConventions.ElementNamesIn(File.ReadAllText(path)))
                .Distinct(StringComparer.Ordinal)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.Not.Empty);
            Assert.That(AutomationConventions.ToolkitElementNames, Is.EquivalentTo(declared),
                "the embedded toolkit XAML is out of step with src/geoDC.App.UI; check ToolkitXaml.props");
        });
    }

    [Test]
    public void Element_names_skip_framework_parts_and_markup_extensions()
    {
        const string xaml = """
            <Border x:Name="Chrome"><Popup x:Name="PART_Popup" /><TextBlock x:Name="{Binding Name}" /></Border>
            """;

        Assert.That(AutomationConventions.ElementNamesIn(xaml), Is.EqualTo(new[] { "Chrome" }));
    }

    /// <summary>
    ///     Matches a declaration of <paramref name="id" /> as a whole token, where <c>*</c> also spans a format
    ///     placeholder, so <c>Step*Button</c> matches the stepper's <c>StringFormat={}Step{0}Button</c>.
    /// </summary>
    private static Regex DeclaredPattern(string id)
    {
        string body = string.Join("[A-Za-z0-9{}]*", id.Split('*').Select(Regex.Escape));
        return new Regex($"(?<![A-Za-z0-9]){body}(?![A-Za-z0-9])", RegexOptions.CultureInvariant);
    }

    private static string ToolkitSource()
    {
        string root = Path.Combine(TestPaths.GeoDCRoot, "src", "geoDC.App.UI");
        return string.Join(Environment.NewLine, ToolkitFiles(root, ".xaml", ".cs").Select(File.ReadAllText));
    }

    private static IEnumerable<string> ToolkitFiles(string root, params string[] extensions)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }
}