using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     The verbs that wait for or check a state rather than change it: <c>wait</c>, <c>assert</c> and <c>settle</c>.
/// </summary>
internal static class CheckVerbs
{
    /// <summary>
    ///     Waits until an element meets a condition. With no condition flag, the element must be present and laid
    ///     out. Use this instead of guessing a <c>settle</c> duration before a screenshot: the script names the
    ///     element whose appearance shows the view has arrived, and waits only as long as that takes. The flags
    ///     (<c>--gone</c>, <c>--enabled</c>, <c>--disabled</c>, <c>--value-matches</c>, <c>--toggled</c>,
    ///     <c>--selected</c>, <c>--expanded</c>) cover the other things a script would otherwise sleep through:
    ///     a popup closing, a command becoming available, a value loading.
    /// </summary>
    internal static void Wait(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        WaitConditions conditions = WaitConditions.Parse(cli);
        string? scopeId = cli.Value("--scope");
        TimeSpan timeout = cli.Timeout(5);

        if (conditions.IsReady)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            (AutomationElement element, bool immediate) = resolver.ResolveReady(id, scopeId, false, timeout);

            System.Drawing.Rectangle bounds = element.Properties.BoundingRectangle.ValueOrDefault;
            JsonOutput.Success("wait", new
            {
                elementId = id,
                // False means the element was in the tree before it had been laid out. That is the case a bare
                // existence check would have raced, so it is worth reporting.
                readyOnFirstLook = immediate,
                waitedMs = (int)stopwatch.ElapsedMilliseconds,
                bounds = $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}"
            }, step);
            return;
        }

        WaitResult result = resolver.WaitFor(id, scopeId, conditions, timeout);
        JsonOutput.Success("wait", new
        {
            elementId = id,
            conditions = conditions.Labels,
            metOnFirstLook = result.MetOnFirstLook,
            waitedMs = result.WaitedMs,
            state = result.Observed.Count == 1 ? result.Observed[0] : null
        }, step);
    }

    /// <summary>
    ///     Checks that an element meets a condition now. The conditions are the same as for <c>wait</c>, polled once
    ///     by default (<c>--timeout</c> allows a grace period). A failure is <c>assert-failed</c>, with the expected
    ///     conditions in the message and the observed state as <c>error.state</c>. Inside <c>do</c> it stops the run
    ///     like any failed step.
    ///     With no condition flag it asserts the element is laid out, as a bare <c>wait</c> does.
    /// </summary>
    internal static void Assert(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        WaitConditions conditions = WaitConditions.Parse(cli);
        string? scopeId = cli.Value("--scope");

        WaitResult result;
        try
        {
            result = resolver.WaitFor(id, scopeId, conditions, cli.Timeout(0));
        }
        catch (DriverException ex) when (ex.Code == "wait-timeout")
        {
            string observed = ex.State switch
            {
                ObservedElementState single => single.Describe(),
                IEnumerable<ObservedElementState> several => string.Join("; ", several.Select(s => $"[{s.Describe()}]")),
                _ => "absent"
            };
            throw new DriverException("assert-failed",
                $"Expected '{id}'" + (scopeId is null ? string.Empty : $" under scope '{scopeId}'") +
                $" to be {string.Join(" + ", conditions.Labels)}, but it was {observed}.", 2, null, ex.State, ex.Lookup);
        }

        JsonOutput.Success("assert", new
        {
            elementId = id,
            conditions = conditions.Labels,
            state = result.Observed.Count == 1 ? result.Observed[0] : null
        }, step);
    }

    internal static void Settle(CliArgs cli, int? step)
    {
        int ms = cli.Int("--ms", 300);
        if (ms is < 0 or > 10_000)
        {
            throw new DriverException("bad-arguments", $"Option '--ms' expects 0..10000 but got {ms}.", 1);
        }

        Thread.Sleep(ms);
        JsonOutput.Success("settle", new { ms }, step);
    }
}