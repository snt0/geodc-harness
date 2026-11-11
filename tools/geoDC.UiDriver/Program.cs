using geoDC.UiDriver.Verbs;

namespace geoDC.UiDriver;

/// <summary>
///     The <c>geodc-ui</c> entry point: takes the output options out of the arguments, runs the verb from
///     <see cref="VerbTable" />, and turns a failure into the verb's failure line and exit code.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        string[] invocation = args;
        OutputTee? tee = null;

        try
        {
            (args, DriverOutputOptions output) =
                DriverOutputOptions.Extract(args, Environment.GetEnvironmentVariable("GEODC_UI_APPEND"),
                    DriverOutputOptions.DefaultFormatFor(Console.IsOutputRedirected));
            JsonOutput.Format = output.Format;
            if (output.AppendPath is { } appendPath)
            {
                tee = OutputTee.Start(appendPath, invocation, DateTime.UtcNow);
            }

            return Run(args, output.ReportPath);
        }
        catch (DriverException ex)
        {
            JsonOutput.Failure(verb, ex.Code, ex.Message, ex.Candidates, null, ex.State, ex.Lookup, ex.Details);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            JsonOutput.Failure(verb, "unexpected-error", ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            // After the catches, so a run that failed outside its steps still reports the failure, and the append file
            // still receives the failure line.
            DoReport.Active?.Save();
            tee?.Dispose();
        }
    }

    /// <summary>
    ///     Runs the verb that <paramref name="args" /> starts with. A verb that parses its own arguments gets them
    ///     whole. For any other verb, the arguments are parsed as options and a warning is added when the target's
    ///     build is newer than the running instance. The verb then runs on its own, or attaches and runs as a session
    ///     verb.
    /// </summary>
    private static int Run(string[] args, string? reportPath)
    {
        DriverVerb? verb = VerbTable.Find(args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty);
        if (verb?.Arguments is { } runWithArguments)
        {
            return runWithArguments(args, reportPath);
        }

        CliArgs cli = new(args);
        JsonOutput.WarnFrom(() => StaleBuildCheck.WarningFor(cli.Value("--target")));
        if (verb is null)
        {
            throw new DriverException("unknown-verb", VerbTable.Usage, 1);
        }

        if (verb.Standalone is { } runStandalone)
        {
            runStandalone(cli);
            return 0;
        }

        using GeoDcSession session = GeoDcSession.Attach(DriverTarget.Parse(cli.Value("--target")));
        DoVerb.ExecuteStep(session, cli, null);
        return 0;
    }
}