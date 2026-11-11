namespace geoDC.UiDriver;

/// <summary>
///     The one refusal every foreground-dependent action gives in a passive launch. A passive app never takes the
///     foreground, so typing or focusing would either fail or pull it forward. The refusal tells the caller how to
///     get an active launch, so the step does not silently do something else.
/// </summary>
public static class PassiveMode
{
    /// <summary>
    ///     The <c>passive-mode</c> error (exit code 2) for <paramref name="action" />, telling the caller to relaunch in
    ///     active mode. The caller throws it.
    /// </summary>
    /// <param name="action">What was refused, as the message's subject, such as <c>key</c>.</param>
    public static DriverException Refusal(string action)
    {
        return new DriverException("passive-mode",
            $"{action} needs the app in the foreground, which a passive launch never gives it. Relaunch in active " +
            "mode (`kill`, then `attach --launch` without --passive) to run it.", 2);
    }
}