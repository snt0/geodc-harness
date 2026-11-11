using System.Windows;
using geoDC.App.UI.Markup;

namespace geoDC.GalleryCorpus.Tests;

/// <summary>
///     The application resources the Sample gallery's <c>App.xaml</c> merges, so its specimen pages can be built in a
///     test: the dark theme, the layout tokens, the sample popup templates and the gallery dictionary. Call it on
///     the STA test thread before creating gallery content.
/// </summary>
internal static class GalleryTestApplication
{
    private static readonly string[] Dictionaries =
    [
        "/geoDC.App.UI;component/Themes/Tokens.Layout.xaml",
        "/geoDC.App.UI.Sample;component/Themes/SamplePopupTemplates.xaml",
        "/geoDC.App.UI.Sample;component/Themes/Gallery.xaml"
    ];

    /// <summary>Returns the current application, creating it if needed, with the gallery's dictionaries merged.</summary>
    public static System.Windows.Application Ensure()
    {
        System.Windows.Application application = System.Windows.Application.Current ?? new System.Windows.Application();
        if (application.Resources.MergedDictionaries.OfType<ThemeDictionary>().All(x => x.Theme != ThemeVariant.Dark))
        {
            application.Resources.MergedDictionaries.Add(new ThemeDictionary { Theme = ThemeVariant.Dark });
        }

        foreach (string dictionary in Dictionaries)
        {
            Uri source = new(dictionary, UriKind.Relative);
            bool alreadyMerged = application.Resources.MergedDictionaries.Any(x => x.Source == source);
            if (alreadyMerged)
            {
                continue;
            }

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = source
            });
        }

        return application;
    }
}