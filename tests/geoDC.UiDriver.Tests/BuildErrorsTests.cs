using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

/// <summary>A failed <c>rebuild</c> reports the build's first errors, read from MSBuild's output lines.</summary>
[TestFixture]
public sealed class BuildErrorsTests
{
    [Test]
    public void A_compiler_error_reads_its_file_location_code_message_and_project()
    {
        BuildError? error = BuildErrors.TryParse(
            @"  C:\src\geodc\src\geoDC.App\Views\MainWindow.xaml.cs(12,5): error CS1002: ; expected [C:\src\geodc\src\geoDC.App\geoDC.App.csproj]");

        Assert.That(error, Is.EqualTo(new BuildError(@"C:\src\geodc\src\geoDC.App\Views\MainWindow.xaml.cs", 12, 5,
            "CS1002", "; expected", @"C:\src\geodc\src\geoDC.App\geoDC.App.csproj")));
    }

    [Test]
    public void A_span_location_keeps_its_start()
    {
        BuildError? error = BuildErrors.TryParse("/repo/src/A.cs(3,7,3,12): error CS0103: The name 'x' does not exist");

        Assert.That(error, Is.EqualTo(new BuildError("/repo/src/A.cs", 3, 7, "CS0103",
            "The name 'x' does not exist", null)));
    }

    [Test]
    public void An_error_without_a_location_names_its_tool_or_project()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BuildErrors.TryParse("MSBUILD : error MSB1009: Project file does not exist."),
                Is.EqualTo(new BuildError("MSBUILD", null, null, "MSB1009", "Project file does not exist.", null)));
            Assert.That(
                BuildErrors.TryParse(
                    @"C:\src\geodc\src\geoDC.App\geoDC.App.csproj : error NU1301: Unable to load the service index. [C:\src\geodc\geoDC.slnx]")
                    ?.File,
                Is.EqualTo(@"C:\src\geodc\src\geoDC.App\geoDC.App.csproj"));
            Assert.That(BuildErrors.TryParse("error CS2001: Source file 'x.cs' could not be found.")?.File, Is.Null);
        });
    }

    [Test]
    public void A_locked_output_copy_is_an_error_with_its_code()
    {
        BuildError? error = BuildErrors.TryParse(
            @"C:\Program Files\dotnet\sdk\10.0.100\Microsoft.Common.CurrentVersion.targets(5034,5): error MSB3027: Could not copy ""obj\Debug\geoDC.App.dll"" to ""bin\Debug\geoDC.App.dll"". The file is locked by: ""geoDC.App (1234)"" [C:\src\geodc\src\geoDC.App\geoDC.App.csproj]");

        Assert.Multiple(() =>
        {
            Assert.That(error?.Code, Is.EqualTo("MSB3027"));
            Assert.That(error?.Line, Is.EqualTo(5034));
            Assert.That(error?.Message, Does.StartWith("Could not copy"));
            Assert.That(error?.Project, Is.EqualTo(@"C:\src\geodc\src\geoDC.App\geoDC.App.csproj"));
        });
    }

    [TestCase(@"C:\src\A.cs(1,1): warning CS0618: 'Foo' is obsolete [C:\src\A.csproj]")]
    [TestCase("Build FAILED.")]
    [TestCase("    3 Error(s)")]
    [TestCase("    0 Warning(s)")]
    [TestCase(@"  geoDC.Domain -> C:\src\geoDC.Domain\bin\Debug\net10.0\geoDC.Domain.dll")]
    [TestCase("")]
    public void Other_lines_are_not_errors(string line)
    {
        Assert.That(BuildErrors.TryParse(line), Is.Null);
    }

    [Test]
    public void Parse_counts_each_distinct_error_once_and_keeps_the_first_in_order()
    {
        string[] lines =
        [
            "/repo/A.cs(1,1): error CS0001: first [/repo/P.csproj]",
            "/repo/A.cs(1,1): warning CS0618: obsolete [/repo/P.csproj]",
            "/repo/B.cs(2,2): error CS0002: second [/repo/P.csproj]",
            "/repo/C.cs(3,3): error CS0003: third [/repo/P.csproj]",
            "Build FAILED.",
            "    /repo/A.cs(1,1): error CS0001: first [/repo/P.csproj]",
            "    /repo/B.cs(2,2): error CS0002: second [/repo/P.csproj]"
        ];

        BuildErrors errors = BuildErrors.Parse(lines, 2);

        Assert.Multiple(() =>
        {
            Assert.That(errors.Count, Is.EqualTo(3));
            Assert.That(errors.First.Select(error => error.Code), Is.EqualTo(new[] { "CS0001", "CS0002" }));
        });
    }
}