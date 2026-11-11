using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     How <c>--window</c>, <c>--main-window</c>, <c>--scope</c> and <c>--id</c> combine to pick the windows or
///     subtree a verb acts on.
/// </summary>
internal static class WindowArguments
{
    /// <summary>
    ///     Resolves the window a window-scoped verb (screenshot, key, tree) acts on from <c>--window</c>,
    ///     defaulting to the main window. <c>--main-window</c> narrows element resolution instead, so the two
    ///     options cannot be combined.
    /// </summary>
    internal static AutomationElement Resolve(GeoDcSession session, CliArgs cli)
    {
        string? selector = cli.Value("--window");
        if (selector is not null && cli.Has("--main-window"))
        {
            throw new DriverException("bad-arguments",
                "--window and --main-window cannot be combined; '--window main' is the explicit form of the default.", 1);
        }

        return session.ResolveWindow(selector);
    }

    /// <summary>
    ///     The top-level windows a whole-window <c>tree</c>, <c>find</c> or <c>audit</c> covers: <c>--window</c>, the main
    ///     window, or all.
    /// </summary>
    internal static AutomationElement[] Selected(GeoDcSession session, CliArgs cli)
    {
        return cli.Value("--window") is not null
            ? [Resolve(session, cli)]
            : cli.Has("--main-window")
                ? [session.MainWindow()]
                : session.TopLevelWindows();
    }

    /// <summary>
    ///     <c>--window</c> selects a window to capture or type into. It does not narrow element resolution
    ///     (<c>--main-window</c> does that), so pairing it with <c>--id</c> is rejected instead of being
    ///     silently ignored.
    /// </summary>
    internal static void RequireNoWindowSelector(CliArgs cli, string because)
    {
        if (cli.Value("--window") is not null)
        {
            throw new DriverException("bad-arguments",
                $"--window cannot be combined with --id: {because}. Use --scope or --main-window to narrow " +
                "element resolution.", 1);
        }
    }

    internal static void RequireNoWindowSelectorWithScope(CliArgs cli)
    {
        if (cli.Value("--window") is not null)
        {
            throw new DriverException("bad-arguments",
                "--window cannot be combined with --scope: the scope element already fixes the subtree wherever it " +
                "lives. Add --main-window to resolve the scope in the main window only.", 1);
        }
    }
}