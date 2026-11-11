using System.Globalization;
using System.IO;
using System.Text;

namespace geoDC.UiDriver;

/// <summary>
///     Copies everything the UI driver writes to stdout and stderr into a file as well (<c>--append &lt;path&gt;</c>, or
///     <c>GEODC_UI_APPEND</c>), so a run can be read or shared without copying it out of the terminal. The file
///     is appended to, never truncated. Each invocation starts with a header naming its UTC start time and its
///     arguments, so several commands, or several sessions, stay apart in one file.
/// </summary>
public sealed class OutputTee : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly StreamWriter _file;

    private OutputTee(TextWriter originalOut, TextWriter originalError, StreamWriter file)
    {
        _originalOut = originalOut;
        _originalError = originalError;
        _file = file;
    }

    /// <summary>
    ///     Starts copying console output to <paramref name="path" />, creating its folder and the file when missing.
    ///     A relative path resolves against the <see cref="DriverHome" />. A file that cannot be opened is reported on
    ///     stderr and the run carries on without the copy.
    /// </summary>
    public static OutputTee? Start(string path, IReadOnlyList<string> args, DateTime startedUtc)
    {
        StreamWriter file;
        try
        {
            string fullPath = DriverHome.Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? DriverHome.Directory);
            file = new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"geodc-ui: could not append output to {path}: {ex.Message}");
            return null;
        }

        file.WriteLine(Header(args, startedUtc));
        OutputTee tee = new(Console.Out, Console.Error, file);
        Console.SetOut(new TeeWriter(tee._originalOut, file));
        Console.SetError(new TeeWriter(tee._originalError, file));
        return tee;
    }

    /// <summary>The line that opens an invocation's output in the file: <c>=== 2026-11-01T14:50:00Z geodc-ui do …</c>.</summary>
    public static string Header(IReadOnlyList<string> args, DateTime startedUtc)
    {
        string arguments = string.Join(' ', args.Select(arg => arg.Length == 0 || arg.Any(char.IsWhiteSpace)
            ? $"\"{arg}\""
            : arg));
        return $"=== {startedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)} geodc-ui {arguments}";
    }

    /// <inheritdoc />
    /// <remarks>Flushes both streams, restores the original console writers, and closes the file.</remarks>
    public void Dispose()
    {
        Console.Out.Flush();
        Console.Error.Flush();
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _file.Dispose();
    }

    /// <summary>Writes to the console writer it replaces and to the file; the console's own encoding is kept.</summary>
    private sealed class TeeWriter(TextWriter console, TextWriter file) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            console.Write(value);
            file.Write(value);
        }

        public override void Write(string? value)
        {
            console.Write(value);
            file.Write(value);
        }

        public override void WriteLine(string? value)
        {
            console.WriteLine(value);
            file.WriteLine(value);
        }

        public override void Flush()
        {
            console.Flush();
            file.Flush();
        }
    }
}