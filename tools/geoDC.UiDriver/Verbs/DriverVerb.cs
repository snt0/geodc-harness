namespace geoDC.UiDriver.Verbs;

/// <summary>
///     One verb of the <c>geodc-ui</c> command line: its name, its usage, and how it runs. A verb sets one of
///     <see cref="Arguments" />, <see cref="Standalone" /> or <see cref="Step" />. A verb with
///     <see cref="Standalone" /> may also set <see cref="Step" /> so that it can run as a step too.
/// </summary>
/// <param name="Name">The verb as typed, in lower case.</param>
/// <param name="Usage">
///     The verb's forms for the usage message, each without the leading <c>geodc-ui</c>, or empty for a verb
///     documented only in the usage notes.
/// </param>
internal sealed record DriverVerb(string Name, IReadOnlyList<string> Usage)
{
    /// <summary>
    ///     Runs the verb from the whole argument list, verb included, with the <c>--report</c> path or null, and returns
    ///     the exit code. For verbs that parse their own arguments: <c>do</c>, <c>rebuild</c> and <c>sample</c>.
    /// </summary>
    public Func<string[], string?, int>? Arguments { get; init; }

    /// <summary>Runs the verb from its parsed options without attaching to an instance.</summary>
    public Action<CliArgs>? Standalone { get; init; }

    /// <summary>
    ///     Runs the verb against an attached instance, with the step number inside a <c>do</c> run or null when it runs
    ///     alone. A verb with only this set attaches before it runs. Every verb with it set can be a <c>do</c> step.
    /// </summary>
    public Action<GeoDcSession, CliArgs, int?>? Step { get; init; }
}