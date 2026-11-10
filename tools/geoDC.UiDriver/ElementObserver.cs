using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace geoDC.UiDriver;

/// <summary>Reads an element's automation state into an <see cref="ObservedElementState" /> for wait polling.</summary>
public static class ElementObserver
{
    /// <summary>
    ///     The element's current state, or <see cref="ObservedElementState.UnreadableNow" /> when a read throws.
    ///     The caller found this element in a fresh search, so a failed read is not proof it has gone: the poll is
    ///     retried, and the next search reports it absent if it has really left the tree.
    /// </summary>
    public static ObservedElementState Observe(AutomationElement element)
    {
        try
        {
            System.Drawing.Rectangle bounds = element.Properties.BoundingRectangle.ValueOrDefault;
            (string? value, bool valueUnreadable) = ReadValue(element);
            return new ObservedElementState(
                true,
                element.Properties.IsOffscreen.ValueOrDefault,
                bounds.Width,
                bounds.Height,
                element.Properties.IsEnabled.ValueOrDefault,
                Optional(() => element.Properties.Name.ValueOrDefault),
                value,
                Optional(() => element.Patterns.Toggle.IsSupported
                    ? element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault switch
                    {
                        ToggleState.On => "on",
                        ToggleState.Off => "off",
                        _ => "indeterminate"
                    }
                    : null),
                OptionalFlag(() => element.Patterns.SelectionItem.IsSupported
                    ? element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault
                    : null),
                Optional(() => element.Patterns.ExpandCollapse.IsSupported
                    ? element.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.ValueOrDefault switch
                    {
                        ExpandCollapseState.Expanded => "expanded",
                        ExpandCollapseState.Collapsed => "collapsed",
                        ExpandCollapseState.PartiallyExpanded => "partially-expanded",
                        _ => "leaf"
                    }
                    : null),
                valueUnreadable);
        }
        catch (Exception)
        {
            return ObservedElementState.UnreadableNow;
        }
    }

    /// <summary>
    ///     The ValuePattern value, keeping "no ValuePattern" (null, not unreadable) apart from "has one but the read
    ///     failed" (unreadable), because only the former lets <c>--value-matches</c> fall back to Name.
    /// </summary>
    private static (string? Value, bool Unreadable) ReadValue(AutomationElement element)
    {
        bool supported;
        try
        {
            supported = element.Patterns.Value.IsSupported;
        }
        catch (Exception)
        {
            return (null, true);
        }

        if (!supported)
        {
            return (null, false);
        }

        try
        {
            return (element.Patterns.Value.Pattern.Value.ValueOrDefault ?? string.Empty, false);
        }
        catch (Exception)
        {
            return (null, true);
        }
    }

    /// <summary>Pattern reads can fail on their own (a provider mid-teardown); losing one field beats losing the poll.</summary>
    private static string? Optional(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? OptionalFlag(Func<bool?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}