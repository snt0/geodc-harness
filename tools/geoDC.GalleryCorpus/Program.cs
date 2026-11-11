using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using CommunityToolkit.Mvvm.Messaging;
using geoDC.App.UI.Markup;
using geoDC.App.UI.Sample.Gallery;
using geoDC.App.UI.Theming;
using SampleApp = geoDC.App.UI.Sample.App;

namespace geoDC.GalleryCorpus;

/// <summary>
///     Writes the Sample gallery's screenshot corpus: every registered specimen rendered offscreen, dark then light,
///     and <c>manifest.json</c>. Live-window specimens are listed without an image.
/// </summary>
internal static class Program
{
    private const string HarnessSolution = "geodc-harness.slnx";

    private const string Usage = """
        Usage: gallery-corpus [--id <ids>] [--control <names>] [--out <dir>] [--width <dip>] [--dpi <dpi>]
                              [--themes dark,light]

          --id       Render only these specimens (comma-separated ids, e.g. ButtonSizesPanel), with the
                     details inside them. Use it to check a template or style change without the app.
          --control  Render only the pages of these controls (comma-separated, e.g. UiButton,UiSection).
          --out      Output folder. Default dev/captures/ds-previews in the harness for the whole corpus,
                     whose theme folders' PNGs and manifest.json are replaced; dev/captures/specimens with
                     --id or --control, where only the rendered images are replaced.
          --width    Page layout width in device-independent pixels (default 1046, a gallery section).
          --dpi      Capture resolution (default 96).
          --themes   Themes to capture, in order (default dark,light).
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        string? outputDirectory = null;
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> controls = new(StringComparer.Ordinal);
        double width = SpecimenCorpusOptions.GallerySectionWidth;
        double dpi = 96d;
        ThemeVariant[] variants = [ThemeVariant.Dark, ThemeVariant.Light];

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option is "-h" or "--help")
            {
                Console.Out.WriteLine(Usage);
                return 0;
            }

            if (index + 1 >= args.Length)
            {
                return Fail($"{option} needs a value.");
            }

            string value = args[++index];
            switch (option)
            {
                case "--out":
                    outputDirectory = value;
                    break;
                case "--id":
                    ids.UnionWith(SplitList(value));
                    break;
                case "--control":
                    controls.UnionWith(SplitList(value));
                    break;
                case "--width" when TryParsePositive(value, out double parsedWidth):
                    width = parsedWidth;
                    break;
                case "--dpi" when TryParsePositive(value, out double parsedDpi):
                    dpi = parsedDpi;
                    break;
                case "--themes" when TryParseThemes(value, out ThemeVariant[] parsedThemes):
                    variants = parsedThemes;
                    break;
                default:
                    return Fail($"Unknown option or bad value: {option} {value}");
            }
        }

        IReadOnlyList<GallerySpecimenRegistration> registrations = GallerySpecimenRegistry.Discover();
        if (controls.Count > 0)
        {
            string[] unknown = controls.Except(registrations.Select(registration => registration.Control.Name)).ToArray();
            if (unknown.Length > 0)
            {
                return Fail($"No specimen page is registered for {string.Join(", ", unknown)}.");
            }

            registrations = registrations.Where(registration => controls.Contains(registration.Control.Name)).ToArray();
        }

        bool filtered = ids.Count > 0 || controls.Count > 0;
        outputDirectory ??= DefaultOutputDirectory(filtered);
        Func<SpecimenCapture, bool>? include = ids.Count == 0
            ? null
            : capture => ids.Contains(capture.Id) || (capture.ParentId is { } parent && ids.Contains(parent));

        SampleApp app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();

        try
        {
            if (ids.Count > 0)
            {
                registrations = PagesNaming(registrations, ids);
            }

            ThemeService themes = new(ThemeDictionaryLocator.FindRequired(), WeakReferenceMessenger.Default);
            SpecimenCorpusExporter exporter = new(themes, new SpecimenPageRenderer(new SpecimenCorpusOptions(width, dpi)));
            SpecimenCorpusExport export = exporter.Export(outputDirectory, registrations, variants,
                ReadCommit(typeof(Specimen).Assembly), include);

            foreach (string failure in export.Failures)
            {
                Console.Error.WriteLine($"FAIL {failure}");
            }

            string[] missing = ids.Except(export.Manifest.Entries.Select(entry => entry.Id)).Order().ToArray();
            foreach (string id in missing)
            {
                Console.Error.WriteLine($"FAIL {id}: no specimen or detail has this id");
            }

            string root = Path.GetFullPath(outputDirectory);
            if (filtered)
            {
                foreach (SpecimenCorpusManifestEntry entry in export.Manifest.Entries)
                {
                    foreach (SpecimenCorpusManifestCapture capture in entry.Captures)
                    {
                        Console.Out.WriteLine(Path.Combine(root, capture.File));
                    }

                    if (entry.RequiresLiveWindow)
                    {
                        Console.Out.WriteLine($"{entry.Id}: needs the live gallery window; not rendered offscreen");
                    }
                }
            }

            int images = export.Manifest.Entries.Sum(entry => entry.Captures.Count);
            int live = export.Manifest.Entries.Count(entry => entry.RequiresLiveWindow);
            Console.Out.WriteLine(
                $"Wrote {images} images for {export.Manifest.Entries.Count} specimens and details to {root}; " +
                $"{live} need the live window (UI driver).");
            return export.Failures.Count == 0 && missing.Length == 0 ? 0 : 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    // The default output folders are in the harness, found above the tool's own build output, so a run from any folder
    // leaves geoDC's tree clean. Without a harness above the tool, they are relative to the working directory.
    private static string DefaultOutputDirectory(bool filtered)
    {
        string relative = Path.Combine("dev", "captures", filtered ? "specimens" : "ds-previews");
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, HarnessSolution)))
            {
                return Path.Combine(directory.FullName, relative);
            }
        }

        return relative;
    }

    // Only pages whose logical tree contains a requested id are rendered. If any requested id is on no page, every page
    // is rendered.
    private static IReadOnlyList<GallerySpecimenRegistration> PagesNaming(
        IReadOnlyList<GallerySpecimenRegistration> registrations, IReadOnlySet<string> ids)
    {
        List<GallerySpecimenRegistration> pages = [];
        HashSet<string> found = new(StringComparer.Ordinal);
        foreach (GallerySpecimenRegistration registration in registrations)
        {
            HashSet<string> named = new(StringComparer.Ordinal);
            CollectNames(registration.CreatePage(), named);
            named.IntersectWith(ids);
            if (named.Count > 0)
            {
                pages.Add(registration);
                found.UnionWith(named);
            }
        }

        return found.IsSupersetOf(ids) ? pages : registrations;
    }

    private static void CollectNames(DependencyObject element, ISet<string> names)
    {
        if (element is Specimen specimen && !string.IsNullOrEmpty(specimen.Id))
        {
            names.Add(specimen.Id);
        }

        string automationId = AutomationProperties.GetAutomationId(element);
        if (!string.IsNullOrEmpty(automationId))
        {
            names.Add(automationId);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject dependencyChild)
            {
                CollectNames(dependencyChild, names);
            }
        }
    }

    private static string[] SplitList(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // Nerdbank.GitVersioning stamps the informational version as <version>+<commit>.
    private static string? ReadCommit(Assembly assembly)
    {
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        int plus = version?.IndexOf('+') ?? -1;
        return version is not null && plus >= 0 ? version[(plus + 1)..] : null;
    }

    private static bool TryParsePositive(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
    }

    private static bool TryParseThemes(string value, out ThemeVariant[] parsed)
    {
        List<ThemeVariant> themes = [];
        foreach (string name in SplitList(value))
        {
            if (!Enum.TryParse(name, true, out ThemeVariant theme) || theme is not (ThemeVariant.Dark or ThemeVariant.Light))
            {
                parsed = [];
                return false;
            }

            themes.Add(theme);
        }

        parsed = themes.ToArray();
        return parsed.Length > 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Usage);
        return 2;
    }
}