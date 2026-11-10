namespace geoDC.UiDriver;

/// <summary>
///     What <c>find</c> searches for. Every given criterion must hold (AND); at least one is required. Matching ignores
///     case throughout.
/// </summary>
/// <param name="Name">A substring of the UIA Name, from <c>--name</c>.</param>
/// <param name="Role">A UIA control type name such as <c>Button</c>, from <c>--role</c>, cased as UIA has it.</param>
/// <param name="Pattern">
///     A supported pattern from <see cref="UiaNodeSnapshot.PatternNames" />, from <c>--pattern</c>, cased as that
///     list spells it.
/// </param>
/// <param name="IdPrefix">A prefix of the AutomationId, from <c>--id-prefix</c>.</param>
public sealed record FindCriteria(string? Name, string? Role, string? Pattern, string? IdPrefix)
{
    /// <summary>
    ///     Reads <c>find</c>'s criteria from <paramref name="cli" />. <paramref name="controlTypes" /> lists the valid
    ///     <c>--role</c> values. Throws <c>bad-arguments</c> when no criterion is given, a value is empty, a role or
    ///     pattern is unknown (listing the valid ones as candidates), or <c>--depth</c> or <c>--id</c> is given:
    ///     <c>find</c> always searches the whole subtree, rooted at <c>--scope</c>.
    /// </summary>
    public static FindCriteria Parse(CliArgs cli, IReadOnlyCollection<string> controlTypes)
    {
        if (cli.Has("--depth"))
        {
            throw new DriverException("bad-arguments",
                "find searches the whole subtree, so --depth does not apply; narrow it with --scope instead.", 1);
        }

        if (cli.Has("--id"))
        {
            throw new DriverException("bad-arguments",
                "find takes --scope <AutomationId> for the subtree to search and --id-prefix to match AutomationIds; " +
                "it has no --id.", 1);
        }

        string? name = NonEmpty(cli, "--name");
        string? idPrefix = NonEmpty(cli, "--id-prefix");
        string? role = Canonical(cli, "--role", controlTypes, "control type");
        string? pattern = Canonical(cli, "--pattern", UiaNodeSnapshot.PatternNames, "pattern");

        if (name is null && role is null && pattern is null && idPrefix is null)
        {
            throw new DriverException("bad-arguments",
                "find needs at least one of --name <substring>, --role <ControlType>, --pattern <Pattern>, or " +
                "--id-prefix <prefix>; use `tree --interactable` to list everything actionable.", 1);
        }

        return new FindCriteria(name, role, pattern, idPrefix);
    }

    /// <summary>
    ///     Whether <paramref name="node" /> meets every non-null criterion, ignoring case: its Name contains
    ///     <see cref="Name" />, its control type equals <see cref="Role" />, it supports <see cref="Pattern" />, and its
    ///     AutomationId starts with <see cref="IdPrefix" /> (a node without an id never matches a prefix).
    /// </summary>
    public bool Matches(UiaNodeSnapshot node)
    {
        return (Name is null || node.Name.Contains(Name, StringComparison.OrdinalIgnoreCase)) &&
               (Role is null || string.Equals(node.ControlType, Role, StringComparison.OrdinalIgnoreCase)) &&
               (Pattern is null || node.Patterns.Contains(Pattern, StringComparer.OrdinalIgnoreCase)) &&
               (IdPrefix is null ||
                (node.AutomationId?.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static string? NonEmpty(CliArgs cli, string option)
    {
        string? value = cli.Value(option);
        return value is { Length: 0 }
            ? throw new DriverException("bad-arguments", $"Option '{option}' expects a non-empty value.", 1)
            : value;
    }

    private static string? Canonical(CliArgs cli, string option, IReadOnlyCollection<string> known, string kind)
    {
        if (NonEmpty(cli, option) is not { } value)
        {
            return null;
        }

        return known.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
               ?? throw new DriverException("bad-arguments",
                   $"Option '{option}' expects a UIA {kind} name but got '{value}'.", 1,
                   [.. known.Order(StringComparer.Ordinal)]);
    }
}