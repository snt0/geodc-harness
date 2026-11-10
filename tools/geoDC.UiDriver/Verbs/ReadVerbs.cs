using System.Globalization;
using System.IO;
using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     The verbs that read the app without changing it: <c>tree</c>, <c>find</c>, <c>source</c>, <c>audit</c>,
///     <c>screenshot</c> and <c>logs</c>.
/// </summary>
internal static class ReadVerbs
{
    /// <summary>
    ///     Dumps the UIA tree under <c>--id</c>, under <c>--scope</c> when there is no <c>--id</c>, or under the selected
    ///     top-level windows. <c>--interactable</c> prints only the interactable outline (see
    ///     <see cref="UiaTreeFilter.KeepInteractable" />) and walks without a depth limit unless <c>--depth</c> is given.
    ///     <para>
    ///         The tree read is stored under <c>.ui-driver/snapshots/</c>, keyed by target and the arguments that decide
    ///         what is read (see <see cref="TreeSnapshotKey" />). It prints in full, or with <c>--diff</c> only what
    ///         changed since the stored snapshot for the same key (see <see cref="UiaTreeDiff" />). A diff with no
    ///         usable snapshot prints the full tree and reports <c>baseline: true</c>.
    ///     </para>
    /// </summary>
    internal static void Tree(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        TreeDumper dumper = new(session);
        TreeOptions options = TreeOptions.Parse(cli);
        bool mainWindowOnly = cli.Has("--main-window");

        IReadOnlyList<UiaNodeSnapshot> roots;
        string header = string.Empty;
        bool separateRoots = false;
        if (cli.Value("--id") is { } id)
        {
            WindowArguments.RequireNoWindowSelector(cli, "--id dumps the element's own subtree wherever it lives");
            AutomationElement element = resolver.Resolve(id, cli.Value("--scope"), mainWindowOnly, cli.Timeout(5));
            roots = [dumper.Read(element, options)];
        }
        else if (cli.Value("--scope") is { } scopeId)
        {
            WindowArguments.RequireNoWindowSelectorWithScope(cli);
            AutomationElement scope = resolver.Resolve(scopeId, null, mainWindowOnly, cli.Timeout(5));
            roots = [dumper.Read(scope, options)];
        }
        else
        {
            AutomationElement[] windows = WindowArguments.Selected(session, cli);
            header = TreeDumper.WindowHeaders(windows);
            separateRoots = true;
            roots = dumper.ReadWindows(windows, options);
        }

        TreeSnapshotStore store = TreeSnapshotStore.Default();
        TreeSnapshotKey key = TreeSnapshotKey.From(session.Target.Name, cli, options);
        IReadOnlyList<UiaNodeSnapshot>? previous = null;
        if (options.Diff)
        {
            TreeSnapshotRead read = store.Read(key);
            if (read.Problem is not null)
            {
                Console.Error.WriteLine(
                    $"geodc-ui: tree snapshot '{store.PathFor(key)}' is unusable ({read.Problem}); printing a new baseline.");
            }

            previous = read.Roots;
        }

        string? snapshotPath;
        try
        {
            snapshotPath = store.Write(key, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The tree itself was read. A snapshot that cannot be stored only costs the next --diff its baseline.
            Console.Error.WriteLine($"geodc-ui: could not store tree snapshot '{store.PathFor(key)}': {ex.Message}");
            snapshotPath = null;
        }

        string[] windowTitles =
            [.. session.TopLevelWindows().Select(w => w.Properties.Name.ValueOrDefault ?? string.Empty)];
        int nodes = roots.Sum(root => 1 + root.CountDescendants());
        if (!options.Diff)
        {
            JsonOutput.Success("tree", new
            {
                windows = windowTitles,
                nodes,
                walked = dumper.WalkedCount,
                depth = options.Depth,
                interactable = options.Interactable,
                snapshot = snapshotPath
            }, step);
            Console.WriteLine(header + TreeDumper.Render(roots, separateRoots));
            return;
        }

        if (previous is null)
        {
            JsonOutput.Success("tree", new
            {
                windows = windowTitles,
                nodes,
                walked = dumper.WalkedCount,
                depth = options.Depth,
                interactable = options.Interactable,
                snapshot = snapshotPath,
                diff = true,
                baseline = true
            }, step);
            Console.WriteLine(header + TreeDumper.Render(roots, separateRoots));
            return;
        }

        UiaTreeDiff diff = UiaTreeDiff.Compare(previous, roots, options.Bounds);
        JsonOutput.Success("tree", new
        {
            windows = windowTitles,
            nodes,
            walked = dumper.WalkedCount,
            depth = options.Depth,
            interactable = options.Interactable,
            snapshot = snapshotPath,
            diff = true,
            baseline = false,
            bounds = options.Bounds,
            added = diff.Added,
            removed = diff.Removed,
            changed = diff.Changed
        }, step);
        Console.Write(diff.Format());
    }

    /// <summary>
    ///     Searches the whole subtree under <c>--scope</c>, or under the selected top-level windows, for nodes matching
    ///     every given criterion, with no depth limit. Prints one <c>tree</c>-format line per match followed by
    ///     <c>path:</c>, the AutomationIds of its ancestors. No match is a success with <c>matches: 0</c>. Use
    ///     <c>assert</c> to require an element.
    /// </summary>
    internal static void Find(GeoDcSession session, CliArgs cli, int? step)
    {
        FindCriteria criteria = FindCriteria.Parse(cli, Enum.GetNames<FlaUI.Core.Definitions.ControlType>());
        string? scopeId = cli.Value("--scope");

        AutomationElement[] roots;
        if (scopeId is not null)
        {
            WindowArguments.RequireNoWindowSelectorWithScope(cli);
            roots = [new ElementResolver(session).Resolve(scopeId, null, cli.Has("--main-window"), cli.Timeout(5))];
        }
        else
        {
            roots = WindowArguments.Selected(session, cli);
        }

        TreeDumper dumper = new(session);
        IReadOnlyList<UiaTreeMatch> matches = dumper.Find(roots, criteria);

        JsonOutput.Success("find", new
        {
            criteria,
            scope = scopeId,
            matches = matches.Count,
            walked = dumper.WalkedCount
        }, step);
        foreach (UiaTreeMatch match in matches)
        {
            Console.WriteLine(match.FormatLine());
        }
    }

    /// <summary>
    ///     Traces the text the app shows back to the strings files: every distinct text (Name, or a ValuePattern value)
    ///     in the subtree of <c>--id</c>, of <c>--scope</c> when there is no <c>--id</c>, or of the selected windows,
    ///     narrowed to those containing <c>--name</c>. Prints one line per text naming the element that shows it, then
    ///     each strings-file entry that matches it (see <see cref="TextSourceCatalog" />) with the lines that read the
    ///     entry. Texts that match no entry are listed too, as data or text built in code. Finding no text is a success
    ///     with <c>texts: 0</c>.
    /// </summary>
    internal static void Source(GeoDcSession session, CliArgs cli, int? step)
    {
        string? id = cli.Value("--id");
        string? scopeId = cli.Value("--scope");
        bool mainWindowOnly = cli.Has("--main-window");
        AutomationElement[] roots;
        if (id is not null)
        {
            WindowArguments.RequireNoWindowSelector(cli, "--id reads the element's own subtree wherever it lives");
            roots = [new ElementResolver(session).Resolve(id, scopeId, mainWindowOnly, cli.Timeout(5))];
        }
        else if (scopeId is not null)
        {
            WindowArguments.RequireNoWindowSelectorWithScope(cli);
            roots = [new ElementResolver(session).Resolve(scopeId, null, mainWindowOnly, cli.Timeout(5))];
        }
        else
        {
            roots = WindowArguments.Selected(session, cli);
        }

        // The catalog is read before the walk, so a worktree without the strings files fails before any UIA work.
        TextSourceCatalog catalog = TextSourceCatalog.Load(Environment.CurrentDirectory);
        TreeDumper dumper = new(session);
        string? contains = cli.Value("--name");
        IReadOnlyList<TextSource> sources =
            [.. TextSourceCatalog.TextsIn(dumper.ReadSubtrees(roots), contains).Select(catalog.Look)];
        TextSourceSummary summary = TextSourceSummary.Of(sources);
        JsonOutput.Success("source", new
        {
            elementId = id,
            scope = scopeId,
            name = contains,
            texts = summary.Texts,
            sourced = summary.Sourced,
            ambiguous = summary.Ambiguous,
            unsourced = summary.Unsourced,
            walked = dumper.WalkedCount
        }, step);
        foreach (TextSource source in sources)
        {
            Console.Write(source.Format());
        }
    }

    /// <summary>
    ///     Walks the whole subtree under <c>--scope</c> (or the selected windows) and checks it against the AutomationId
    ///     conventions (see <see cref="AutomationAudit" />). With no findings it succeeds with <c>data.findings</c>
    ///     empty. Otherwise it fails with <c>audit-findings</c>, the same fields on <c>error</c>, and exit code
    ///     <see cref="AutomationAudit.FindingsExitCode" />, so a <c>do</c> run stops there like a failed <c>assert</c>.
    /// </summary>
    internal static void Audit(GeoDcSession session, CliArgs cli, int? step)
    {
        if (cli.Has("--id") || cli.Has("--depth"))
        {
            throw new DriverException("bad-arguments",
                "audit walks the whole subtree under --scope <AutomationId> (or the selected windows); it takes no --id " +
                "or --depth.", 1);
        }

        string? scopeId = cli.Value("--scope");
        AutomationElement[] roots;
        if (scopeId is not null)
        {
            WindowArguments.RequireNoWindowSelectorWithScope(cli);
            roots = [new ElementResolver(session).Resolve(scopeId, null, cli.Has("--main-window"), cli.Timeout(5))];
        }
        else
        {
            roots = WindowArguments.Selected(session, cli);
        }

        TreeDumper dumper = new(session);
        AuditReport report = AutomationAudit.Run(dumper.ReadSubtrees(roots));
        if (report.ToFailure(scopeId, dumper.WalkedCount) is { } failure)
        {
            throw failure;
        }

        JsonOutput.Success("audit", report.Summary(scopeId, dumper.WalkedCount), step);
    }

    internal static void Screenshot(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        bool mainWindowOnly = cli.Has("--main-window");
        string? elementId = cli.Value("--id");
        string? region = cli.Value("--region");
        if (elementId is not null && region is not null)
        {
            throw new DriverException("bad-arguments", "Use either --id or --region, not both.", 1);
        }

        ScreenshotOptions options = ScreenshotOptions.Parse(cli);

        AutomationElement window;
        System.Drawing.Rectangle? crop;
        if (elementId is not null)
        {
            WindowArguments.RequireNoWindowSelector(cli, "--id already captures the element's own top-level window");
            AutomationElement element = resolver.Resolve(elementId, cli.Value("--scope"), mainWindowOnly, cli.Timeout(5));
            window = resolver.TopLevelAncestor(element);
            crop = Screenshotter.ElementCrop(window, element);
        }
        else
        {
            window = WindowArguments.Resolve(session, cli);
            crop = region is not null ? ParseRegion(region) : null;
        }

        // Highlights resolve like --id (same --scope and --main-window); one in another top-level window is
        // reported as not visible, since PrintWindow renders only the captured window.
        IntPtr capturedHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
        List<HighlightTarget> highlights = [];
        foreach (string highlightId in options.HighlightIds)
        {
            AutomationElement highlighted =
                resolver.Resolve(highlightId, cli.Value("--scope"), mainWindowOnly, cli.Timeout(5));
            bool inCapturedWindow =
                resolver.TopLevelAncestor(highlighted).Properties.NativeWindowHandle.ValueOrDefault == capturedHandle;
            highlights.Add(new HighlightTarget(highlightId, highlighted.Properties.BoundingRectangle.ValueOrDefault,
                inCapturedWindow));
        }

        ScreenshotCapture capture = Screenshotter.CaptureWindow(window, crop, options, highlights, cli.Value("--out"));
        JsonOutput.Success("screenshot", new
        {
            path = capture.Path,
            width = capture.Width,
            height = capture.Height,
            originalWidth = capture.OriginalWidth,
            originalHeight = capture.OriginalHeight,
            scale = Math.Round(capture.Scale, 4),
            maxWidth = options.MaxWidth,
            pad = options.PadDips,
            padPixels = capture.PadPixels,
            elementId,
            window = GeoDcSession.DescribeWindow(window),
            highlights = capture.Highlights.Select(h => new
            {
                elementId = h.ElementId,
                visible = h.Visible,
                clipped = h.Clipped,
                bounds = h.Bounds is { } b ? $"{b.X},{b.Y} {b.Width}x{b.Height}" : null,
                reason = h.Reason
            })
        }, step);
    }

    private static System.Drawing.Rectangle ParseRegion(string region)
    {
        string[] parts = region.Split(',');
        if (parts.Length == 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
        {
            return new System.Drawing.Rectangle(x, y, w, h);
        }

        throw new DriverException("bad-arguments",
            $"Option '--region' expects window-relative physical pixels 'x,y,w,h' but got '{region}'.", 1);
    }

    /// <summary>
    ///     Tails the target's newest Serilog file (logs/geodc-*.log next to the exe, falling back to
    ///     ./logs). It needs no running instance, so crash logs stay readable after the process dies.
    /// </summary>
    internal static void Logs(CliArgs cli, int? step)
    {
        DriverTarget target = DriverTarget.Parse(cli.Value("--target"));
        int tail = cli.Int("--tail", 60);

        List<string> searched = [];
        FileInfo? newest = null;
        string?[] candidates =
        [
            target.OpensProject ? AppDataRoot.LogsDirectory() : null,
            GeoDcSession.TryResolveExeDirectory(target) is { } exeDir ? Path.Combine(exeDir, "logs") : null,
            "logs"
        ];
        foreach (string? dir in candidates)
        {
            if (dir is null || !Directory.Exists(dir))
            {
                continue;
            }

            searched.Add(Path.GetFullPath(dir));
            foreach (FileInfo file in new DirectoryInfo(dir).GetFiles("geodc-*.log"))
            {
                if (newest is null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                {
                    newest = file;
                }
            }
        }

        if (newest is null)
        {
            throw new DriverException("no-logs",
                $"No geodc-*.log found under: {(searched.Count > 0 ? string.Join("; ", searched) : "(no logs directory)")}. " +
                "The app writes logs once launched; the sample host has no file log.", 2);
        }

        // The app's shared:true sink keeps the file open, so it is read with full sharing.
        List<string> lines = [];
        using (FileStream stream = new(newest.FullName, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (StreamReader reader = new(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
        }

        string[] tailed = [.. lines.Skip(Math.Max(0, lines.Count - tail))];
        JsonOutput.Success("logs", new { path = newest.FullName, totalLines = lines.Count, tailed = tailed.Length },
            step);
        foreach (string line in tailed)
        {
            Console.WriteLine(line);
        }
    }
}