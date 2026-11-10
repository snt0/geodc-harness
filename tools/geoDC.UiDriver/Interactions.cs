using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;

namespace geoDC.UiDriver;

/// <summary>
///     Pattern-based interaction. Actions use UIA patterns, not coordinate clicks, so they work on
///     scrolled-away elements and do not depend on layout.
/// </summary>
public static partial class Interactions
{
    /// <summary>Invoke, falling back to Toggle / SelectionItem for control types that expose those instead.</summary>
    public static string Click(AutomationElement element, string automationId)
    {
        RequireEnabled(element, automationId, "click");

        if (element.Patterns.Invoke.IsSupported)
        {
            DriverTrace.Write("click", "invoke-call");
            element.Patterns.Invoke.Pattern.Invoke();
            DriverTrace.Write("click", "invoke-returned");
            return "invoke";
        }

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return "toggle";
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return "selection-item";
        }

        if (element.Patterns.ExpandCollapse.IsSupported)
        {
            IExpandCollapsePattern pattern = element.Patterns.ExpandCollapse.Pattern;
            if (pattern.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Expanded)
            {
                pattern.Collapse();
                return "collapse";
            }

            pattern.Expand();
            return "expand";
        }

        throw new DriverException("not-invokable",
            $"Element '{automationId}' supports none of Invoke/Toggle/SelectionItem/ExpandCollapse; " +
            "it cannot be clicked via patterns.",
            2);
    }

    /// <summary>
    ///     Expands a collapsed ExpandCollapse container (e.g. a ComboBox) so its items realize,
    ///     focusing its top-level window first, because WPF dropdowns close as soon as the window loses
    ///     foreground. Returns whether an expand actually happened. A passive launch never gives up the
    ///     foreground, so there the expand is refused rather than attempted.
    /// </summary>
    public static bool TryExpand(AutomationElement container, AutomationElement topLevelWindow, bool passive)
    {
        bool collapsed;
        try
        {
            collapsed = container.Patterns.ExpandCollapse.IsSupported &&
                        container.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault ==
                        ExpandCollapseState.Collapsed;
        }
        catch (Exception)
        {
            return false;
        }

        if (!collapsed)
        {
            return false;
        }

        if (passive)
        {
            throw PassiveMode.Refusal("select on a collapsed drop-down (it must be focused to stay open)");
        }

        try
        {
            topLevelWindow.Focus();
            container.Patterns.ExpandCollapse.Pattern.Expand();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     Acts on a name-resolved item: SelectionItemPattern.Select when supported, otherwise the
    ///     standard click fallback chain (Invoke/Toggle/ExpandCollapse). Scrolls into view first
    ///     when possible so a follow-up screenshot shows the item.
    /// </summary>
    public static (string Method, bool ScrolledIntoView) SelectItem(AutomationElement item, string name)
    {
        RequireEnabled(item, name, "select");

        bool scrolledIntoView = false;
        try
        {
            if (item.Patterns.ScrollItem.IsSupported)
            {
                item.Patterns.ScrollItem.Pattern.ScrollIntoView();
                scrolledIntoView = true;
            }
        }
        catch (Exception)
        {
            // Scroll-into-view is best-effort; pattern actions work on offscreen items regardless.
        }

        if (item.Patterns.SelectionItem.IsSupported)
        {
            item.Patterns.SelectionItem.Pattern.Select();
            return ("selection-item", scrolledIntoView);
        }

        return (Click(item, name), scrolledIntoView);
    }

    /// <summary>
    ///     Sends one key chord (e.g. "Escape", "Ctrl+S") to whatever currently has keyboard focus.
    ///     The caller is responsible for bringing a geoDC window to the foreground first.
    /// </summary>
    public static string SendKeys(string chord)
    {
        string[] parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new DriverException("bad-arguments", "Option '--keys' expects a key chord such as 'Escape' or 'Ctrl+S'.", 1);
        }

        VirtualKeyShort[] keys = [.. parts.Select(ParseKey)];
        if (keys.Length == 1)
        {
            Keyboard.Type(keys[0]);
        }
        else
        {
            Keyboard.TypeSimultaneously(keys);
        }

        return string.Join("+", keys);
    }

    private static VirtualKeyShort ParseKey(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "ctrl" or "control":
                return VirtualKeyShort.CONTROL;
            case "alt":
                return VirtualKeyShort.ALT;
            case "shift":
                return VirtualKeyShort.SHIFT;
            case "win":
                return VirtualKeyShort.LWIN;
            case "escape" or "esc":
                return VirtualKeyShort.ESCAPE;
            case "enter" or "return":
                return VirtualKeyShort.ENTER;
            case "space":
                return VirtualKeyShort.SPACE;
            case "backspace":
                return VirtualKeyShort.BACK;
            case "delete" or "del":
                return VirtualKeyShort.DELETE;
            case "pageup":
                return VirtualKeyShort.PRIOR;
            case "pagedown":
                return VirtualKeyShort.NEXT;
        }

        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            return Enum.Parse<VirtualKeyShort>($"KEY_{char.ToUpperInvariant(name[0])}");
        }

        return Enum.TryParse(name, true, out VirtualKeyShort key)
            ? key
            : throw new DriverException("bad-arguments",
                $"Unknown key '{name}' in '--keys'. Use names like Escape, Enter, Tab, F5, Ctrl+S, Alt+F4.", 1);
    }

    /// <summary>
    ///     ValuePattern.SetValue, falling back to focus + keystrokes only when the pattern is absent. Keystrokes need
    ///     the foreground, so a passive launch refuses that fallback.
    /// </summary>
    public static string SetText(AutomationElement element, string automationId, string value, bool passive)
    {
        RequireEnabled(element, automationId, "settext");

        if (element.Patterns.Value.IsSupported)
        {
            if (element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
            {
                throw new DriverException("element-read-only",
                    $"Element '{automationId}' is read-only; its value cannot be set.", 2);
            }

            element.Patterns.Value.Pattern.SetValue(value);
            return "value-pattern";
        }

        if (passive)
        {
            throw PassiveMode.Refusal($"settext on '{automationId}' (it has no ValuePattern, so it must be typed into)");
        }

        element.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
        return "keystrokes";
    }

    private static void RequireEnabled(AutomationElement element, string automationId, string action)
    {
        if (!element.Properties.IsEnabled.ValueOrDefault)
        {
            throw new DriverException("element-disabled",
                $"Element '{automationId}' is disabled; {action} would have no effect. " +
                "Check the tree for the enabling precondition instead of retrying.", 2);
        }
    }
}