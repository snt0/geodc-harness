using FlaUI.Core.AutomationElements;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     The verbs that drive the app: <c>click</c>, <c>settext</c>, <c>select</c>, <c>scroll</c>, <c>scrollto</c> and
///     <c>key</c>, with the <c>--await</c> and <c>--await-gone</c> checks that follow an action.
/// </summary>
internal static class ActionVerbs
{
    internal static void Click(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        AwaitPlan awaits = AwaitPlan.Parse(cli);
        AutomationElement element = resolver.Resolve(id, cli.Value("--scope"), false, cli.Timeout(5));

        string method = Interactions.Click(element, id);
        IReadOnlyList<object> awaited = RunAwaits(resolver, awaits, "click");
        JsonOutput.Success("click", new { elementId = id, method }, step, awaited);
    }

    internal static void SetText(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        string value = cli.Require("--value");
        AwaitPlan awaits = AwaitPlan.Parse(cli);
        AutomationElement element = resolver.Resolve(id, cli.Value("--scope"), false, cli.Timeout(5));

        string method = Interactions.SetText(element, id, value, session.Passive);
        IReadOnlyList<object> awaited = RunAwaits(resolver, awaits, "settext");
        JsonOutput.Success("settext", new { elementId = id, method, value }, step, awaited);
    }

    /// <summary>
    ///     Selects a data-bound item (no AutomationId) inside a container by UIA Name. The names come
    ///     from the view-models' ToString overrides. `select --id JobList --item "Stage 2"`.
    /// </summary>
    internal static void Select(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string containerId = cli.Require("--id");
        string itemName = cli.Require("--item");
        TimeSpan timeout = cli.Timeout(5);
        AwaitPlan awaits = AwaitPlan.Parse(cli);

        AutomationElement container = resolver.Resolve(containerId, cli.Value("--scope"), false, timeout);

        // A collapsed combo has no realized items, and its dropdown only stays open while the app owns the
        // foreground. So the window is focused, the combo expanded, and the item resolved while it is open.
        bool expandedContainer = false;
        if (Interactions.TryExpand(container, resolver.TopLevelAncestor(container), session.Passive))
        {
            expandedContainer = true;
        }

        (AutomationElement item, string match) = resolver.ResolveItemByName(container, containerId, itemName, timeout);

        // Read before selecting: closing a combo's dropdown can leave the item's peer stale by the time awaits finish.
        string selectedName = item.Properties.Name.ValueOrDefault ?? itemName;
        (string method, bool scrolledIntoView) = Interactions.SelectItem(item, itemName);
        IReadOnlyList<object> awaited = RunAwaits(resolver, awaits, "select");
        JsonOutput.Success("select", new
        {
            containerId,
            item = selectedName,
            match,
            method,
            scrolledIntoView,
            expandedContainer
        }, step, awaited);
    }

    internal static void Scroll(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        string direction = cli.Require("--direction");
        string amount = cli.Value("--amount") ?? "large";
        AutomationElement element = resolver.Resolve(id, cli.Value("--scope"), false, cli.Timeout(5));

        Interactions.ScrollResult result = Interactions.Scroll(element, id, direction, amount);
        JsonOutput.Success("scroll", new
        {
            elementId = id,
            direction = result.Direction,
            amount = result.Amount,
            horizontalPercentBefore = result.HorizontalPercentBefore,
            horizontalPercentAfter = result.HorizontalPercentAfter,
            verticalPercentBefore = result.VerticalPercentBefore,
            verticalPercentAfter = result.VerticalPercentAfter
        }, step);
    }

    internal static void ScrollTo(GeoDcSession session, CliArgs cli, int? step)
    {
        ElementResolver resolver = new(session);
        string id = cli.Require("--id");
        AutomationElement element = resolver.Resolve(id, cli.Value("--scope"), false, cli.Timeout(5));

        string method = Interactions.ScrollIntoView(element, id, resolver, session.Passive);
        System.Drawing.Rectangle bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        JsonOutput.Success("scrollto", new
        {
            elementId = id,
            method,
            offscreen = element.Properties.IsOffscreen.ValueOrDefault,
            bounds = $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}"
        }, step);
    }

    internal static void Key(GeoDcSession session, CliArgs cli, int? step)
    {
        string chord = cli.Require("--keys");
        AwaitPlan awaits = AwaitPlan.Parse(cli);
        if (session.Passive)
        {
            throw PassiveMode.Refusal("key");
        }

        AutomationElement window = WindowArguments.Resolve(session, cli);

        // An explicit --window is used as given. Only the default target follows the modal chain, so
        // `key --keys Escape` still reaches a modal popup that blocks the main window.
        if (cli.Value("--window") is null)
        {
            window = session.ResolveInputTarget(window);
        }

        // Described before activation: re-reading Name straight after a foreground change intermittently
        // comes back empty, which would report a nameless window for a selector that matched on its title.
        string described = GeoDcSession.DescribeWindow(window);

        // Keys go through SendInput, so they land wherever the system focus is. The target window must be in
        // the foreground first, and a plain SetFocus or SetForegroundWindow from this process cannot put it there.
        bool foreground = WindowActivation.Activate(window);
        try
        {
            window.Focus();
        }
        catch (Exception)
        {
            // Placing keyboard focus inside the window is best-effort. Activation is what routes the chord.
        }

        // When the window is not in the foreground, the verb refuses to type. Otherwise SendInput would deliver
        // the chord to whatever is (a terminal, another app) and the JSON would still say ok.
        if (!foreground)
        {
            throw new DriverException("window-not-foreground",
                $"Could not bring {described} to the foreground, so the chord would " +
                "have gone to another application. A locked, RDP-disconnected, or screensaver-blanked session " +
                "cannot be given the foreground at all.", 2);
        }

        string sent = Interactions.SendKeys(chord);
        IReadOnlyList<object> awaited = RunAwaits(new ElementResolver(session), awaits, "key");
        JsonOutput.Success("key", new { keys = sent, window = described }, step, awaited);
    }

    /// <summary>
    ///     Runs an action verb's <c>--await-gone</c> / <c>--await</c> waits after the action, so the action and the
    ///     proof it landed are one step. Await targets resolve across every window, ignoring the action's
    ///     <c>--scope</c>: what an action opens usually lives somewhere else (a popup is its own window). A failed
    ///     await fails the step with the wait's own error code, but says that the action itself already happened.
    /// </summary>
    internal static IReadOnlyList<object> RunAwaits(ElementResolver resolver, AwaitPlan plan, string verb)
    {
        List<object> awaited = [];
        foreach (AwaitRequest request in plan.Requests)
        {
            string option = request.Conditions.IsGone ? "--await-gone" : "--await";
            try
            {
                // Both kinds go through WaitFor, which re-searches every poll: an action often replaces the very
                // control being awaited, and a held handle would keep polling the old one.
                WaitResult result = resolver.WaitFor(request.ElementId, null, request.Conditions, plan.Timeout);
                awaited.Add(new
                {
                    elementId = request.ElementId,
                    condition = request.Conditions.IsGone ? "gone" : "ready",
                    metOnFirstLook = result.MetOnFirstLook,
                    waitedMs = result.WaitedMs
                });
            }
            catch (DriverException ex)
            {
                throw new DriverException(ex.Code,
                    $"The {verb} was performed, but {option} {request.ElementId} failed: {ex.Message}", ex.ExitCode,
                    ex.Candidates, ex.State, ex.Lookup);
            }
        }

        return awaited;
    }
}