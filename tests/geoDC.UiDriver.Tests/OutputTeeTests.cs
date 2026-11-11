using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
[NonParallelizable]
public sealed class OutputTeeTests
{
    private static readonly DateTime Started = new(2026, 11, 1, 14, 50, 0, DateTimeKind.Utc);

    [Test]
    public void Output_reaches_the_console_and_is_appended_to_the_file_after_a_header()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ui-driver-tee-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "nested", "run.log");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter console = new();
        using StringWriter error = new();
        try
        {
            Console.SetOut(console);
            Console.SetError(error);
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.GetDirectoryName(path)!).FullName, "run.log"),
                "earlier run" + Environment.NewLine);

            using (OutputTee.Start(path, ["do", "--file", "x y.ui"], Started))
            {
                Console.WriteLine("{\"ok\":true}");
                Console.Error.WriteLine("geodc-ui: a diagnostic");
            }

            Console.WriteLine("after the tee");
            string[] lines = File.ReadAllLines(path);

            Assert.Multiple(() =>
            {
                Assert.That(lines, Is.EqualTo(new[]
                {
                    "earlier run",
                    "=== 2026-11-01T14:50:00Z geodc-ui do --file \"x y.ui\"",
                    "{\"ok\":true}",
                    "geodc-ui: a diagnostic"
                }), "the file is appended to, with stdout and stderr after the header");
                Assert.That(console.ToString(), Does.Contain("{\"ok\":true}").And.Contain("after the tee"));
                Assert.That(error.ToString(), Does.Contain("geodc-ui: a diagnostic"));
            });
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Test]
    public void A_file_that_cannot_be_opened_is_reported_and_the_run_carries_on()
    {
        TextWriter originalError = Console.Error;
        using StringWriter error = new();
        try
        {
            Console.SetError(error);

            OutputTee? tee = OutputTee.Start("\0bad", ["status"], Started);

            Assert.Multiple(() =>
            {
                Assert.That(tee, Is.Null);
                Assert.That(error.ToString(), Does.Contain("could not append output"));
            });
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}