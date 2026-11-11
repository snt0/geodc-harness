namespace geoDC.UiDriver.Tests;

/// <summary>
///     The checkouts the tests read from: the harness, found by its solution above the test output, and the geoDC clone
///     nested in it at <c>geodc/</c>.
/// </summary>
internal static class TestPaths
{
    private const string HarnessSolution = "geodc-harness.slnx";

    /// <summary>The harness root, which holds <c>dev/ui-scripts/</c>.</summary>
    public static string HarnessRoot { get; } = FindHarnessRoot();

    /// <summary>The geoDC repository root, which holds the strings files and the XAML the conventions mirror.</summary>
    public static string GeoDCRoot => Path.Combine(HarnessRoot, "geodc");

    private static string FindHarnessRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, HarnessSolution)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException($"{HarnessSolution} not found above the tests.");
    }
}