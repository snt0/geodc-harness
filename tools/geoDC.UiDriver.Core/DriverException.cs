namespace geoDC.UiDriver;

/// <summary>
///     A failure that maps directly onto the UI driver's JSON <c>error</c> object. It always has an error code and a
///     process exit code (2 = element not found, ambiguous or in the wrong state, 3 = attach failure,
///     4 = <c>audit</c> findings, 1 = anything else). The other properties are optional: near-match candidates, a
///     structured <see cref="State" />, <see cref="Lookup" /> hints for an element that could not be resolved, and
///     <see cref="Details" />. Each property's documentation says which <c>error</c> fields it fills.
/// </summary>
public sealed class DriverException(
    string code,
    string message,
    int exitCode,
    IReadOnlyList<string>? candidates = null,
    object? state = null,
    ElementLookupHints? lookup = null,
    object? details = null) : Exception(message)
{
    /// <summary>The machine-readable error code emitted as <c>error.code</c>, such as <c>bad-arguments</c>.</summary>
    public string Code { get; } = code;

    /// <summary>
    ///     The process exit code: 2 for an element not found, ambiguous or in the wrong state, 3 for an attach
    ///     failure, 4 for <c>audit</c> findings, 1 for anything else.
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>Near-match candidates emitted as <c>error.candidates</c>, or null when there are none to offer.</summary>
    public IReadOnlyList<string>? Candidates { get; } = candidates;

    /// <summary>
    ///     A structured observation emitted as <c>error.state</c>, such as a timed-out wait's last observed element
    ///     state, or null when there is none.
    /// </summary>
    public object? State { get; } = state;

    /// <summary>
    ///     Hints for an element that could not be resolved, emitted as <c>error.closest</c>, <c>error.elsewhere</c>,
    ///     <c>error.matches</c> and <c>error.hint</c>, or null when the failure is not a lookup.
    /// </summary>
    public ElementLookupHints? Lookup { get; } = lookup;

    /// <summary>
    ///     An object whose properties are added to <c>error</c> as they are, such as a failed build's errors, or null
    ///     when there are none.
    /// </summary>
    public object? Details { get; } = details;
}