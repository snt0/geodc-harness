namespace geoDC.UiDriver.Verbs;

/// <summary>
///     <c>do</c>: runs steps against one attached instance, and the step execution every session verb shares, whether
///     it runs alone or as a step.
/// </summary>
internal static class DoVerb
{
    /// <summary>
    ///     Runs several steps against one shared session: `do [--target t] click --id A ++ settext --id B --value x`,
    ///     or `do [--target t] --file script.ui [--var name=value ...]` for a checked-in script (see
    ///     <see cref="DoInvocation" /> for its grammar). It prints one JSON line per executed step (1-based `step`
    ///     field). It stops at the first failure and exits with that step's exit code, so later steps are not
    ///     reported. A final summary line counts the steps run and passed and gives the first failure.
    /// </summary>
    internal static int Run(string[] args, string? reportPath)
    {
        DoInvocation invocation = DoInvocation.Parse(args[1..], DriverHome.ReadAllText);
        if (reportPath is not null)
        {
            DoReport.Active = new DoReport(reportPath, invocation, DateTime.UtcNow);
        }

        JsonOutput.WarnFrom(() => StaleBuildCheck.WarningFor(invocation.Target));
        using GeoDcSession session = GeoDcSession.Attach(DriverTarget.Parse(invocation.Target));
        return RunSteps(session, invocation);
    }

    /// <summary>
    ///     Runs a parsed <c>do</c> invocation's steps against <paramref name="session" />, one JSON line each (with
    ///     <c>from</c> when the step came from a script), then the summary line. Stops at the first failure and returns
    ///     its exit code, or 0 when every step passed.
    /// </summary>
    internal static int RunSteps(GeoDcSession session, DoInvocation invocation)
    {
        IReadOnlyList<DoStep> steps = invocation.Steps;
        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            int stepNumber = stepIndex + 1;
            DoStep current = steps[stepIndex];
            JsonOutput.SetStepOrigin(current.From);
            JsonOutput.BeginStep();
            CliArgs step;
            try
            {
                step = new CliArgs(current.Tokens);
                if (step.Value("--target") is not null)
                {
                    throw new DriverException("bad-arguments",
                        "--target applies to the whole `do` invocation; place it before the first step.", 1);
                }

                if (step.Verb == "pause")
                {
                    RunPause(session, step, stepNumber, invocation.Interactive, current.From);
                }
                else
                {
                    ExecuteStep(session, step, stepNumber);
                }
            }
            catch (DriverException ex)
            {
                string stepVerb = current.Tokens[0].ToLowerInvariant();
                JsonOutput.Failure(stepVerb, ex.Code, ex.Message, ex.Candidates, stepNumber, ex.State, ex.Lookup,
                    ex.Details);
                JsonOutput.SetStepOrigin(null);
                JsonOutput.EndStep();
                JsonOutput.DoSummary(steps.Count, stepNumber, stepNumber - 1, invocation.ScriptPath,
                    current.From.Count > 0
                        ? (object)new { step = stepNumber, verb = stepVerb, code = ex.Code, from = current.From }
                        : new { step = stepNumber, verb = stepVerb, code = ex.Code });
                return ex.ExitCode;
            }
        }

        JsonOutput.SetStepOrigin(null);
        JsonOutput.EndStep();
        JsonOutput.DoSummary(steps.Count, steps.Count, steps.Count, invocation.ScriptPath, null);
        return 0;
    }

    /// <summary>
    ///     Runs a <c>pause</c> step (see <see cref="PauseStep" />). In an interactive run it first brings geoDC's main
    ///     window forward, since a passive session keeps it behind other windows, and it runs outside
    ///     <see cref="ForegroundGuard" />: while paused the developer owns the foreground, and the app must not be
    ///     pushed back when they answer.
    /// </summary>
    private static void RunPause(GeoDcSession session, CliArgs cli, int step, bool interactive, IReadOnlyList<string> from)
    {
        string? location = from.Count > 0 ? from[^1] : null;
        if (location is not null && location.StartsWith(DoInvocation.ScriptRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            location = location[(DoInvocation.ScriptRoot.Length + 1)..];
        }

        if (interactive)
        {
            WindowActivation.Activate(session.MainWindow());
        }

        JsonOutput.Success("pause", PauseStep.Run(cli, interactive, new PausePromptWindow(), step, location), step);
    }

    /// <summary>
    ///     Runs a session verb against <paramref name="session" />, alone or as step <paramref name="step" /> of a
    ///     <c>do</c> run, inside the trace markers and the <see cref="ForegroundGuard" /> every session verb shares.
    ///     A verb that cannot run as a step fails with <c>bad-arguments</c>.
    /// </summary>
    internal static void ExecuteStep(GeoDcSession session, CliArgs cli, int? step)
    {
        DriverTrace.Enable(session.Process.Id);
        DriverTrace.Write(cli.Verb, "step-start");
        ForegroundGuard? guard = ForegroundGuard.Begin(session);
        try
        {
            Action<GeoDcSession, CliArgs, int?> run = VerbTable.Find(cli.Verb)?.Step ??
                                                       throw new DriverException("bad-arguments",
                                                           $"'{cli.Verb}' is not a valid step verb — use " +
                                                           $"{VerbTable.StepVerbList} (attach is implicit).", 1);
            run(session, cli, step);
        }
        finally
        {
            DriverTrace.Write(cli.Verb, "step-end");
            guard?.End(cli.Verb);
        }
    }
}