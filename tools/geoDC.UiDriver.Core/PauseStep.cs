namespace geoDC.UiDriver;

/// <summary>What a <c>pause</c> step shows the developer.</summary>
/// <param name="Step">The step's number in the run.</param>
/// <param name="Message">What to check, from <c>--message</c>; null when the script gave none.</param>
/// <param name="Location">The script line the step came from (<c>lib/x.ui:3</c>), or null for an inline step.</param>
/// <param name="Timeout">How long to wait for an answer, or null to wait indefinitely.</param>
public sealed record PauseRequest(int Step, string? Message, string? Location, TimeSpan? Timeout);

/// <summary>How the developer answered a <c>pause</c>.</summary>
public enum PauseVerdict
{
    /// <summary>The check passed; the run carries on.</summary>
    Continued,

    /// <summary>The check failed, or the prompt was closed without an answer; the run stops.</summary>
    Failed,

    /// <summary>Nobody answered within the step's <c>--timeout</c>.</summary>
    TimedOut
}

/// <summary>The developer's answer to a <c>pause</c>, with the note they typed (null when empty).</summary>
public sealed record PauseAnswer(PauseVerdict Verdict, string? Note, long WaitedMs);

/// <summary>Shows a <see cref="PauseRequest" /> to the developer and waits for their answer.</summary>
public interface IPausePrompt
{
    /// <summary>Blocks until the developer answers, closes the prompt, or the request's timeout passes.</summary>
    PauseAnswer Ask(PauseRequest request);
}

/// <summary>
///     The <c>pause [--message &lt;text&gt;] [--timeout &lt;s&gt;]</c> step of a <c>do</c> run: a manual check. In an
///     interactive run (<c>do --interactive</c>) it asks the developer through an <see cref="IPausePrompt" /> and
///     passes or fails on their verdict. Otherwise it passes at once with <c>skipped: true</c>, so the same script
///     runs unattended. The prompt is injected, so the outcomes can be tested without a window.
/// </summary>
public static class PauseStep
{
    /// <summary>The exit code of a failed or unanswered manual check, as for any check the app did not meet.</summary>
    public const int FailedExitCode = 2;

    /// <summary>
    ///     Runs the step and returns its JSON <c>data</c>: <c>message</c>, <c>skipped</c>, <c>verdict</c>
    ///     (<c>continued</c>, or null when skipped), <c>note</c> and <c>waitedMs</c>. A Fail answer throws
    ///     <c>manual-check-failed</c> and an unanswered prompt <c>pause-timeout</c>, both carrying the note and the
    ///     wait in <c>error</c>.
    /// </summary>
    public static object Run(CliArgs cli, bool interactive, IPausePrompt prompt, int step, string? location)
    {
        string? message = cli.Value("--message");
        TimeSpan? timeout = cli.Has("--timeout") ? cli.Timeout(0) : null;
        if (timeout is { } given && given <= TimeSpan.Zero)
        {
            throw new DriverException("bad-arguments", "pause --timeout expects a positive number of seconds.", 1);
        }

        if (!interactive)
        {
            return new { message, skipped = true, verdict = (string?)null, note = (string?)null, waitedMs = 0L };
        }

        PauseAnswer answer = prompt.Ask(new PauseRequest(step, message, location, timeout));
        string? note = string.IsNullOrWhiteSpace(answer.Note) ? null : answer.Note.Trim();
        string subject = message is null ? "the manual check" : $"the manual check \"{message}\"";
        switch (answer.Verdict)
        {
            case PauseVerdict.Continued:
                return new { message, skipped = false, verdict = "continued", note, waitedMs = answer.WaitedMs };
            case PauseVerdict.TimedOut:
                throw new DriverException("pause-timeout",
                    $"Nobody answered {subject} within {timeout?.TotalSeconds:0.#}s.", FailedExitCode,
                    details: new { note, waitedMs = answer.WaitedMs });
            default:
                string because = note is null ? "." : $": {note}";
                throw new DriverException("manual-check-failed", $"The developer failed {subject}{because}",
                    FailedExitCode, details: new { note, waitedMs = answer.WaitedMs });
        }
    }
}