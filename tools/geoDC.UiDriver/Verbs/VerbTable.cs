namespace geoDC.UiDriver.Verbs;

/// <summary>
///     Every <c>geodc-ui</c> verb, in the order the usage message lists them. The entry point and <c>do</c> both
///     dispatch through it, and the usage message and the list of step verbs are generated from it, so a new verb is
///     one entry here.
/// </summary>
internal static class VerbTable
{
    private const string Condition =
        "[--exists] [--enabled | --disabled] [--value <exact> | --value-matches <regex>] [--name <exact>] " +
        "[--toggled on|off|indeterminate] [--selected] [--expanded]";

    /// <summary>The verbs, in usage order.</summary>
    public static IReadOnlyList<DriverVerb> All { get; } =
    [
        new("attach", ["attach [--target app|sample] [--launch [--project <path>] [--passive]] [--timeout <s>]"])
        {
            Standalone = SessionVerbs.Attach
        },
        new("kill", ["kill [--target app|sample]"]) { Standalone = SessionVerbs.Kill },
        new("status", ["status [--target app|sample]"]) { Standalone = SessionVerbs.Status },
        new("sample", ["sample create [--lifecycle]", "sample status"])
        {
            Arguments = (args, _) =>
            {
                SampleProjectVerb.Run(args);
                return 0;
            }
        },
        new("rebuild",
        [
            "rebuild [--target app|sample] [--project <path>] [--passive | --active] [--timeout <s>] " +
            "[--then <script> [--var name=value ...]]"
        ]) { Arguments = SessionVerbs.Rebuild },
        new("tree",
        [
            "tree [--id <AutomationId>] [--scope <AutomationId>] [--depth <n>] [--interactable] [--main-window] " +
            "[--window <selector>] [--diff [--bounds]]"
        ]) { Step = ReadVerbs.Tree },
        new("find",
        [
            "find [--name <substring>] [--role <ControlType>] [--pattern <Pattern>] [--id-prefix <prefix>] " +
            "[--scope <AutomationId>] [--main-window] [--window <selector>]"
        ]) { Step = ReadVerbs.Find },
        new("source",
        [
            "source [--id <AutomationId>] [--scope <AutomationId>] [--name <substring>] [--main-window] " +
            "[--window <selector>]"
        ]) { Step = ReadVerbs.Source },
        new("audit", ["audit [--scope <AutomationId>] [--main-window] [--window <selector>]"])
        {
            Step = ReadVerbs.Audit
        },
        new("screenshot",
        [
            "screenshot [--id <AutomationId> [--pad <dip>] | --region x,y,w,h] [--max-width <px=1600, 0=off>] " +
            "[--highlight <AutomationId>[,<AutomationId>...]] [--out <path>] [--main-window] [--window <selector>]"
        ]) { Step = ReadVerbs.Screenshot },
        new("click", ["click --id <AutomationId> [--scope <AutomationId>] [<await>]"]) { Step = ActionVerbs.Click },
        new("settext", ["settext --id <AutomationId> --value <text> [--scope <AutomationId>] [<await>]"])
        {
            Step = ActionVerbs.SetText
        },
        new("select", ["select --id <container> --item <name> [--scope <AutomationId>] [<await>]"])
        {
            Step = ActionVerbs.Select
        },
        new("scroll",
        [
            "scroll --id <container> --direction up|down|left|right|top|bottom [--amount small|large] " +
            "[--scope <AutomationId>]"
        ]) { Step = ActionVerbs.Scroll },
        new("scrollto", ["scrollto --id <AutomationId> [--scope <AutomationId>]"]) { Step = ActionVerbs.ScrollTo },
        new("wait", ["wait --id <AutomationId> [--scope <AutomationId>] [--timeout <s>] [<condition>]"])
        {
            Step = CheckVerbs.Wait
        },
        new("assert", ["assert --id <AutomationId> [--scope <AutomationId>] [--timeout <s=0>] [<condition>]"])
        {
            Step = CheckVerbs.Assert
        },
        new("key", ["key --keys <chord> [--window <selector>] [<await>]"]) { Step = ActionVerbs.Key },
        new("settle", ["settle --ms <n>"])
        {
            Standalone = cli => CheckVerbs.Settle(cli, null),
            Step = (_, cli, step) => CheckVerbs.Settle(cli, step)
        },
        new("logs", ["logs [--tail <n>] [--target app|sample]"])
        {
            Standalone = cli => ReadVerbs.Logs(cli, null),
            Step = (_, cli, step) => ReadVerbs.Logs(cli, step)
        },
        new("do",
        [
            "do [--target app|sample] [--interactive] <step> ++ <step> ++ ...",
            "do [--target app|sample] [--interactive] --file <script> [--var name=value ...]"
        ]) { Arguments = DoVerb.Run },
        // A do step only: the step loop runs it (DoVerb.RunSteps), so alone it explains where it belongs.
        new("pause", [])
        {
            Standalone = _ => throw new DriverException("bad-arguments",
                "pause is a step of a do run: do --interactive ... ++ pause --message <text>.", 1)
        }
    ];

    /// <summary>The step verbs as a sentence, e.g. <c>tree, find, ..., settle, or logs</c>.</summary>
    public static string StepVerbList
    {
        get
        {
            string[] names = StepVerbNames();
            return string.Join(", ", names[..^1]) + ", or " + names[^1];
        }
    }

    /// <summary>
    ///     The usage message an unknown verb fails with: every verb's forms, then the notes that define the
    ///     placeholders.
    /// </summary>
    public static string Usage =>
        "Usage: geodc-ui " + string.Join(" | ", All.SelectMany(verb => verb.Usage)) + " " +
        $"(<condition> is --gone | {Condition}) " +
        "(<await> is [--await <AutomationId>] [--await-gone <AutomationId>] [--await-timeout <s>]) " +
        "(--window selects a top-level window: main | title:<substring> | handle:0x…) " +
        "(do also takes --report <path.md>; every verb takes --format json|text and --append <path>) " +
        $"(steps are {string.Join("/", StepVerbNames())}, " +
        "run <script> [name=value ...], which runs another script's steps in their place, and " +
        "pause [--message <text>] [--timeout <s>], a manual check that waits only with --interactive)";

    /// <summary>The verb named <paramref name="name" /> (already lower case), or null when there is none.</summary>
    public static DriverVerb? Find(string name)
    {
        return All.FirstOrDefault(verb => verb.Name == name);
    }

    private static string[] StepVerbNames()
    {
        return [.. All.Where(verb => verb.Step is not null).Select(verb => verb.Name)];
    }
}