using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using geoDC.App.UI.Markup;
using geoDC.App.UI.Sample.Gallery;
using geoDC.App.UI.Sample.Specimens.Actions;
using geoDC.App.UI.Sample.Specimens.Chrome;
using geoDC.App.UI.Sample.Specimens.Collections;
using geoDC.App.UI.Sample.Specimens.Foundations;
using geoDC.App.UI.Sample.Specimens.Overlays;
using geoDC.App.UI.Sample.Specimens.Sections;
using geoDC.App.UI.Theming;

namespace geoDC.GalleryCorpus.Tests;

/// <summary>
///     The screenshot corpus renders every specimen offscreen, so these render the real gallery pages: each must
///     produce an image without the gallery window, and two exports must match byte for byte.
/// </summary>
public sealed class SpecimenCorpusTests
{
    private static readonly SpecimenCorpusOptions Options = new();

    [Test]
    public void Every_specimen_that_does_not_need_the_live_window_renders_offscreen()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();
            SpecimenPageRenderer renderer = new(Options);
            List<string> problems = [];

            foreach (GallerySpecimenRegistration registration in GallerySpecimenRegistry.Discover())
            {
                foreach (SpecimenCapture capture in renderer.Render(registration).Captures)
                {
                    if (capture.RequiresLiveWindow)
                    {
                        continue;
                    }

                    if (capture.Error is not null || capture.Image is not { PixelWidth: > 0, PixelHeight: > 0 })
                    {
                        problems.Add($"{registration.PageType.Name}/{capture.Id}: {capture.Error ?? "no image"}");
                    }
                }
            }

            Assert.That(problems, Is.Empty);
        });
    }

    [Test]
    public void Only_specimens_that_need_the_gallery_window_services_are_left_for_the_ui_driver()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();
            SpecimenPageRenderer renderer = new(Options);

            string[] live = GallerySpecimenRegistry.Discover()
                .SelectMany(registration => renderer.Render(registration).Captures)
                .Where(capture => capture.RequiresLiveWindow)
                .Select(capture => capture.Id)
                .ToArray();

            Assert.That(live, Is.EquivalentTo(new[]
            {
                "NotificationPlaygroundPanel", "LoadingLaunchersPanel", "FeedbackBehaviourPanel",
                "PopupLaunchersPanel", "DialogLaunchersPanel"
            }));
        });
    }

    [Test]
    public void A_page_is_loaded_before_it_is_rendered()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();

            SpecimenPageRender render = new SpecimenPageRenderer(Options).Render(Registration<UiListViewSpecimens>());

            Assert.That(render.WasLoaded, Is.True);
        });
    }

    [Test]
    public void A_specimen_capture_is_its_frame_at_the_layout_width_and_dpi()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();
            GallerySpecimenRegistration typography = Registration<UiTextBlockSpecimens>();

            SpecimenCapture at96 = new SpecimenPageRenderer(Options).Render(typography).Captures[0];
            SpecimenCapture at192 = new SpecimenPageRenderer(Options with { Dpi = 192d }).Render(typography).Captures[0];

            Assert.Multiple(() =>
            {
                Assert.That(at96.Image?.PixelWidth, Is.EqualTo((int)Options.Width));
                Assert.That(at192.Image?.PixelWidth, Is.EqualTo(at96.Image?.PixelWidth * 2));
                Assert.That(at192.Image?.PixelHeight, Is.EqualTo(at96.Image?.PixelHeight * 2));
            });
        });
    }

    [Test]
    public void Details_inside_a_specimen_are_captured_after_it_and_name_it_as_parent()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();

            IReadOnlyList<SpecimenCapture> captures =
                new SpecimenPageRenderer(Options).Render(Registration<UiSectionSpecimens>()).Captures;
            string[] ids = captures.Select(capture => capture.Id).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(ids, Is.SupersetOf(new[] { "SectionFlushPanel", "OverviewPanel", "FiltersPanel" }));
                Assert.That(Array.IndexOf(ids, "OverviewPanel"), Is.EqualTo(Array.IndexOf(ids, "SectionFlushPanel") + 1));
                Assert.That(captures.Single(capture => capture.Id == "FiltersPanel").ParentId,
                    Is.EqualTo("SectionFlushPanel"));
                Assert.That(captures.Single(capture => capture.Id == "SectionFlushPanel").ParentId, Is.Null);
            });
        });
    }

    [Test]
    public void Details_are_the_ids_a_page_declares_not_the_parts_of_a_control_template()
    {
        StaTestRunner.Run(() =>
        {
            _ = GalleryTestApplication.Ensure();
            SpecimenPageRenderer renderer = new(Options);

            string[] dockPane = renderer.Render(Registration<UiDockPaneSpecimens>()).Captures
                .Select(capture => capture.Id).ToArray();
            string[] drawer = renderer.Render(Registration<UiOverlayDrawerSpecimens>()).Captures
                .Select(capture => capture.Id).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(dockPane, Is.SupersetOf(new[] { "DockPanePanel", "ToolbarPanel", "RibbonGroupPanel" }));
                Assert.That(drawer, Is.EqualTo(new[] { "SampleDrawerPanel" }),
                    "The drawer template's own panel id is not a figure of the page.");
            });
        });
    }

    [Test]
    public void A_full_export_with_fewer_themes_removes_the_other_themes_images()
    {
        string directory = NewDirectory();
        try
        {
            StaTestRunner.Run(() =>
            {
                _ = GalleryTestApplication.Ensure();
                ThemeService themes = new(ThemeDictionaryLocator.FindRequired(), new WeakReferenceMessenger());
                SpecimenCorpusExporter exporter = new(themes, new SpecimenPageRenderer(Options));
                GallerySpecimenRegistration[] pages = [Registration<UiButtonSpecimens>()];

                exporter.Export(directory, pages, [ThemeVariant.Dark, ThemeVariant.Light], null);
                exporter.Export(directory, pages, [ThemeVariant.Dark], null);
            });

            Assert.That(RelativeFiles(directory).Where(file => file.StartsWith("light/", StringComparison.Ordinal)),
                Is.Empty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Two_exports_from_the_same_build_are_byte_identical_and_restore_the_theme()
    {
        string first = NewDirectory();
        string second = NewDirectory();
        try
        {
            StaTestRunner.Run(() =>
            {
                _ = GalleryTestApplication.Ensure();
                ThemeService themes = new(ThemeDictionaryLocator.FindRequired(), new WeakReferenceMessenger());
                ThemeVariant before = themes.Current;
                SpecimenCorpusExporter exporter = new(themes, new SpecimenPageRenderer(Options));
                GallerySpecimenRegistration[] pages = ExportPages();
                ThemeVariant[] variants = [ThemeVariant.Dark, ThemeVariant.Light];

                SpecimenCorpusExport export = exporter.Export(first, pages, variants, "0123456789");
                exporter.Export(second, pages, variants, "0123456789");

                Assert.Multiple(() =>
                {
                    Assert.That(export.Failures, Is.Empty);
                    Assert.That(themes.Current, Is.EqualTo(before));
                });
            });

            string[] files = RelativeFiles(first);
            Assert.Multiple(() =>
            {
                Assert.That(files, Does.Contain("manifest.json").And.Contain("dark/ButtonVariantsPanel.png")
                    .And.Contain("light/ButtonVariantsPanel.png"));
                Assert.That(RelativeFiles(second), Is.EqualTo(files));
                foreach (string file in files)
                {
                    Assert.That(File.ReadAllBytes(Path.Combine(second, file)),
                        Is.EqualTo(File.ReadAllBytes(Path.Combine(first, file))), file);
                }
            });
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    [Test]
    public void The_manifest_describes_each_specimen_and_leaves_live_ones_for_the_ui_driver()
    {
        string directory = NewDirectory();
        try
        {
            StaTestRunner.Run(() =>
            {
                _ = GalleryTestApplication.Ensure();
                ThemeService themes = new(ThemeDictionaryLocator.FindRequired(), new WeakReferenceMessenger());
                new SpecimenCorpusExporter(themes, new SpecimenPageRenderer(Options))
                    .Export(directory, ExportPages(), [ThemeVariant.Dark], "0123456789");
            });

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            JsonElement root = manifest.RootElement;
            JsonElement[] entries = root.GetProperty("entries").EnumerateArray().ToArray();
            JsonElement button = entries.Single(entry => entry.GetProperty("id").GetString() == "ButtonVariantsPanel");
            JsonElement filters = entries.Single(entry => entry.GetProperty("id").GetString() == "FiltersPanel");
            JsonElement popup = entries.Single(entry => entry.GetProperty("id").GetString() == "PopupLaunchersPanel");
            JsonElement capture = button.GetProperty("captures")[0];

            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("commit").GetString(), Is.EqualTo("0123456789"));
                Assert.That(button.GetProperty("kind").GetString(), Is.EqualTo("specimen"));
                Assert.That(button.GetProperty("control").GetString(), Is.EqualTo("UiButton"));
                Assert.That(button.GetProperty("group").GetString(), Is.EqualTo("Actions"));
                Assert.That(button.GetProperty("navItemId").GetString(), Is.EqualTo("ActionsNavItem"));
                Assert.That(button.GetProperty("caption").GetString(), Is.Not.Empty);
                Assert.That(capture.GetProperty("theme").GetString(), Is.EqualTo("dark"));
                Assert.That(capture.GetProperty("file").GetString(), Is.EqualTo("dark/ButtonVariantsPanel.png"));
                Assert.That(capture.GetProperty("source").GetString(), Is.EqualTo("offscreen"));
                Assert.That(capture.GetProperty("pixelWidth").GetInt32(), Is.GreaterThan(0));
                Assert.That(capture.GetProperty("pixelsSha256").GetString(), Has.Length.EqualTo(64));
                Assert.That(filters.GetProperty("kind").GetString(), Is.EqualTo("detail"));
                Assert.That(filters.GetProperty("parent").GetString(), Is.EqualTo("SectionFlushPanel"));
                Assert.That(popup.GetProperty("requiresLiveWindow").GetBoolean(), Is.True);
                Assert.That(popup.GetProperty("captures").GetArrayLength(), Is.Zero);
                Assert.That(File.Exists(Path.Combine(directory, "dark", "PopupLaunchersPanel.png")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void A_filtered_export_writes_only_the_picked_specimen_and_its_details_and_keeps_other_images()
    {
        string directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "dark"));
            File.WriteAllBytes(Path.Combine(directory, "dark", "EarlierPanel.png"), [1, 2, 3]);

            SpecimenCorpusExport export = StaTestRunner.Run(() =>
            {
                _ = GalleryTestApplication.Ensure();
                ThemeService themes = new(ThemeDictionaryLocator.FindRequired(), new WeakReferenceMessenger());
                return new SpecimenCorpusExporter(themes, new SpecimenPageRenderer(Options))
                    .Export(directory, ExportPages(), [ThemeVariant.Dark], null,
                        capture => capture.Id == "SectionFlushPanel" || capture.ParentId == "SectionFlushPanel");
            });

            Assert.Multiple(() =>
            {
                Assert.That(export.Failures, Is.Empty);
                Assert.That(export.Manifest.Entries.Select(entry => entry.Id),
                    Is.EqualTo(new[] { "SectionFlushPanel", "OverviewPanel", "FiltersPanel", "SelectionPanel" }));
                Assert.That(RelativeFiles(directory), Is.EqualTo(new[]
                {
                    "dark/EarlierPanel.png", "dark/FiltersPanel.png", "dark/OverviewPanel.png",
                    "dark/SectionFlushPanel.png", "dark/SelectionPanel.png", "manifest.json"
                }));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static GallerySpecimenRegistration[] ExportPages()
    {
        return
        [
            Registration<UiButtonSpecimens>(), Registration<UiTextBlockSpecimens>(),
            Registration<UiSectionSpecimens>(), Registration<UiDataGridSpecimens>(), Registration<PopupSpecimens>()
        ];
    }

    private static GallerySpecimenRegistration Registration<TPage>()
        where TPage : SpecimenPage
    {
        return GallerySpecimenRegistry.Discover().Single(registration => registration.PageType == typeof(TPage));
    }

    private static string NewDirectory()
    {
        return Directory.CreateTempSubdirectory("geodc-corpus-").FullName;
    }

    private static string[] RelativeFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}