namespace geoDC.UiDriver;

/// <summary>
///     <c>rebuild</c>'s arguments: its own options, and the <c>--then &lt;script&gt; [--var name=value ...]</c> script it
///     runs after a relaunch.
/// </summary>
public static class RebuildArguments
{
    /// <summary>
    ///     Splits <c>rebuild</c>'s arguments into its own options and the <c>--then</c> script, parsed for the target
    ///     <c>rebuild</c> uses. The script name resolves as a <c>run</c> step's does
    ///     (<see cref="DoInvocation.ResolveScriptPath" />). <c>--var</c> may repeat and, like <c>--interactive</c>,
    ///     applies only with <c>--then</c>. <paramref name="readFile" /> loads the script, as for
    ///     <see cref="DoInvocation.Parse" />.
    /// </summary>
    public static (string[] RebuildArgs, DoInvocation? Then) Split(IReadOnlyList<string> args,
        Func<string, string> readFile)
    {
        List<string> rebuildArgs = [args[0]];
        List<string> doArgs = [];
        string? script = null;
        for (int i = 1; i < args.Count; i++)
        {
            string option = args[i].ToLowerInvariant();
            if (option == "--interactive")
            {
                doArgs.Add("--interactive");
                continue;
            }

            if (option is "--then" or "--var")
            {
                if (i + 1 >= args.Count)
                {
                    throw new DriverException("bad-arguments", $"Option '{args[i]}' expects a value.", 1);
                }

                if (option == "--then")
                {
                    script = args[++i];
                }
                else
                {
                    doArgs.Add("--var");
                    doArgs.Add(args[++i]);
                }

                continue;
            }

            rebuildArgs.Add(args[i]);
        }

        if (script is null)
        {
            return doArgs.Count > 0
                ? throw new DriverException("bad-arguments", "--var and --interactive apply only with --then <script>.", 1)
                : (rebuildArgs.ToArray(), null);
        }

        string target = DriverTarget.Parse(new CliArgs([.. rebuildArgs]).Value("--target")).Name;
        DoInvocation then = DoInvocation.Parse(
            ["--target", target, "--file", DoInvocation.ResolveScriptPath(script, null), .. doArgs], readFile);
        return (rebuildArgs.ToArray(), then);
    }
}