namespace geoDC.UiDriver;

/// <summary>
///     The <c>screenshot</c> verb's output-shaping options: <c>--max-width</c>, <c>--pad</c> and <c>--highlight</c>.
///     Parsed and validated without touching UI Automation, so a bad combination fails before anything is captured.
/// </summary>
/// <param name="MaxWidth">
///     Widest output in pixels; a wider capture is downscaled proportionally and a narrower one is never upscaled.
///     Zero disables the limit.
/// </param>
/// <param name="PadDips">
///     Device-independent pixels added to each side of an <c>--id</c> crop, clamped to the window. Zero for no padding.
/// </param>
/// <param name="HighlightIds">AutomationIds to outline in the capture, in the order given, without duplicates.</param>
public sealed record ScreenshotOptions(int MaxWidth, int PadDips, IReadOnlyList<string> HighlightIds)
{
    /// <summary>
    ///     Output width limit when <c>--max-width</c> is absent: wide enough to read a toolkit control at 100% scaling,
    ///     small enough that a 200% full-window capture stays cheap to read.
    /// </summary>
    public const int DefaultMaxWidth = 1600;

    /// <summary>
    ///     Reads the options from a <c>screenshot</c> invocation.
    /// </summary>
    /// <exception cref="DriverException">
    ///     <c>bad-arguments</c> when a value is negative or not an integer, when <c>--pad</c> is given without
    ///     <c>--id</c>, or when <c>--highlight</c> names no AutomationId.
    /// </exception>
    public static ScreenshotOptions Parse(CliArgs cli)
    {
        int maxWidth = cli.Int("--max-width", DefaultMaxWidth);
        if (maxWidth < 0)
        {
            throw new DriverException("bad-arguments",
                $"Option '--max-width' expects a pixel width of 0 or more (0 disables it) but got {maxWidth}.", 1);
        }

        int pad = cli.Int("--pad", 0);
        if (pad < 0)
        {
            throw new DriverException("bad-arguments",
                $"Option '--pad' expects device-independent pixels of 0 or more but got {pad}.", 1);
        }

        if (cli.Has("--pad") && cli.Value("--id") is null)
        {
            throw new DriverException("bad-arguments",
                "--pad grows an --id crop; a --region or full-window capture already chooses its own extent.", 1);
        }

        List<string> highlightIds = [];
        if (cli.Value("--highlight") is { } rawHighlight)
        {
            foreach (string part in rawHighlight.Split(',',
                         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!highlightIds.Contains(part, StringComparer.Ordinal))
                {
                    highlightIds.Add(part);
                }
            }

            if (highlightIds.Count == 0)
            {
                throw new DriverException("bad-arguments",
                    $"Option '--highlight' expects one or more comma-separated AutomationIds but got '{rawHighlight}'.",
                    1);
            }
        }

        return new ScreenshotOptions(maxWidth, pad, highlightIds);
    }
}