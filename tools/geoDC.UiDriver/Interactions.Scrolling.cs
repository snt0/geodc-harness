using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;

namespace geoDC.UiDriver;

// Scrolling a container by pattern, and bringing an element into its scroll viewport.
public static partial class Interactions
{
    /// <summary>UIA's sentinel for "this axis does not scroll", required by SetScrollPercent.</summary>
    private const double NoScroll = -1;

    /// <summary>
    ///     What <see cref="Scroll" /> did, with the UIA scroll percentages (0 to 100, or -1 for an axis that does not
    ///     scroll) read before and after.
    /// </summary>
    /// <param name="Direction">The lower-case direction: <c>up</c>, <c>down</c>, <c>left</c>, <c>right</c>, <c>top</c> or <c>bottom</c>.</param>
    /// <param name="Amount">The lower-case increment, <c>small</c> or <c>large</c>, or <c>endpoint</c> for <c>top</c> and <c>bottom</c>.</param>
    /// <param name="HorizontalPercentBefore">The horizontal scroll percentage before scrolling.</param>
    /// <param name="HorizontalPercentAfter">The horizontal scroll percentage after scrolling.</param>
    /// <param name="VerticalPercentBefore">The vertical scroll percentage before scrolling.</param>
    /// <param name="VerticalPercentAfter">The vertical scroll percentage after scrolling.</param>
    public sealed record ScrollResult(
        string Direction,
        string Amount,
        double HorizontalPercentBefore,
        double HorizontalPercentAfter,
        double VerticalPercentBefore,
        double VerticalPercentAfter);

    /// <summary>
    ///     Scrolls a UIA Scroll provider by a small/large increment or directly to its vertical endpoint.
    ///     Direction is semantic rather than coordinate-based, so it remains stable across DPI and layout changes.
    /// </summary>
    public static ScrollResult Scroll(AutomationElement element, string automationId, string direction,
        string amount)
    {
        if (!element.Patterns.Scroll.IsSupported)
        {
            throw new DriverException("not-scrollable",
                $"Element '{automationId}' does not expose UIA ScrollPattern. " +
                "Target the owning ScrollViewer/panel AutomationId, or use scrollto on an item that exposes ScrollItemPattern.",
                2);
        }

        IScrollPattern pattern = element.Patterns.Scroll.Pattern;
        string normalizedDirection = direction.ToLowerInvariant();
        string normalizedAmount = amount.ToLowerInvariant();
        ScrollAmount increment = normalizedAmount switch
        {
            "small" => ScrollAmount.SmallIncrement,
            "large" => ScrollAmount.LargeIncrement,
            _ => throw new DriverException("bad-arguments",
                $"Option '--amount' expects 'small' or 'large' but got '{amount}'.", 1)
        };
        ScrollAmount decrement = normalizedAmount == "small"
            ? ScrollAmount.SmallDecrement
            : ScrollAmount.LargeDecrement;

        double horizontalBefore = pattern.HorizontalScrollPercent.ValueOrDefault;
        double verticalBefore = pattern.VerticalScrollPercent.ValueOrDefault;

        switch (normalizedDirection)
        {
            case "up":
                RequireScrollable(pattern.VerticallyScrollable.ValueOrDefault, automationId, "vertically");
                pattern.Scroll(ScrollAmount.NoAmount, decrement);
                break;
            case "down":
                RequireScrollable(pattern.VerticallyScrollable.ValueOrDefault, automationId, "vertically");
                pattern.Scroll(ScrollAmount.NoAmount, increment);
                break;
            case "left":
                RequireScrollable(pattern.HorizontallyScrollable.ValueOrDefault, automationId, "horizontally");
                pattern.Scroll(decrement, ScrollAmount.NoAmount);
                break;
            case "right":
                RequireScrollable(pattern.HorizontallyScrollable.ValueOrDefault, automationId, "horizontally");
                pattern.Scroll(increment, ScrollAmount.NoAmount);
                break;
            case "top":
                RequireScrollable(pattern.VerticallyScrollable.ValueOrDefault, automationId, "vertically");
                pattern.SetScrollPercent(horizontalBefore, 0);
                normalizedAmount = "endpoint";
                break;
            case "bottom":
                RequireScrollable(pattern.VerticallyScrollable.ValueOrDefault, automationId, "vertically");
                pattern.SetScrollPercent(horizontalBefore, 100);
                normalizedAmount = "endpoint";
                break;
            default:
                throw new DriverException("bad-arguments",
                    $"Option '--direction' expects up, down, left, right, top, or bottom but got '{direction}'.", 1);
        }

        return new ScrollResult(normalizedDirection, normalizedAmount, horizontalBefore,
            pattern.HorizontalScrollPercent.ValueOrDefault, verticalBefore,
            pattern.VerticalScrollPercent.ValueOrDefault);
    }

    /// <summary>
    ///     Brings an element into view through ScrollItemPattern. Focus is a useful fallback for focusable WPF
    ///     controls because SetFocus asks their owning ScrollViewer to reveal them. Neither works for a plain
    ///     layout panel: WPF grants ScrollItemPattern only to generated item containers (ListBoxItem,
    ///     DataGridRow, TreeViewItem), and layout panels take no focus. So the last strategy drives the owning
    ///     viewport's ScrollPattern directly, converting the element's screen bounds into a scroll percentage.
    /// </summary>
    public static string ScrollIntoView(AutomationElement element, string automationId, ElementResolver resolver,
        bool passive)
    {
        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            return "scroll-item";
        }

        // Focusing would take the foreground, which a passive launch never gives up. The viewport strategy below
        // reaches the same element without it.
        if (!passive && element.Properties.IsKeyboardFocusable.ValueOrDefault)
        {
            element.Focus();
            return "focus";
        }

        if (resolver.NearestScrollableAncestor(element) is { } viewport)
        {
            ScrollViewportTo(element, viewport);
            return "viewport";
        }

        throw new DriverException("not-scrollable-item",
            $"Element '{automationId}' exposes no UIA ScrollItemPattern, takes no keyboard focus, and sits in " +
            "no scroll container, so there is nothing to scroll. It is either already fully visible or clipped " +
            "by a non-scrolling parent.", 2);
    }

    /// <summary>
    ///     Scrolls <paramref name="viewport" /> until <paramref name="element" /> is fully visible, leaving a
    ///     small margin so the element is not flush against the edge. It runs a few passes, because revealing
    ///     content can change layout (wrapping panels re-flow, virtualized rows realize) and move the target.
    ///     Re-measuring after each scroll converges where a single computed jump would not.
    /// </summary>
    private static void ScrollViewportTo(AutomationElement element, AutomationElement viewport)
    {
        const int marginPixels = 24;
        const int maxPasses = 4;

        IScrollPattern pattern = viewport.Patterns.Scroll.Pattern;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            System.Drawing.Rectangle target = element.Properties.BoundingRectangle.ValueOrDefault;
            System.Drawing.Rectangle view = viewport.Properties.BoundingRectangle.ValueOrDefault;

            if (target.Height <= 0 || view.Height <= 0)
            {
                return;
            }

            double horizontal = pattern.HorizontalScrollPercent.ValueOrDefault;
            double vertical = pattern.VerticalScrollPercent.ValueOrDefault;

            if (!pattern.VerticallyScrollable.ValueOrDefault)
            {
                return;
            }

            double viewSize = pattern.VerticalViewSize.ValueOrDefault;
            if (viewSize is <= 0 or >= 100)
            {
                return;
            }

            // UIA reports no absolute content size, so the scrollable extent is derived from the viewport height
            // and the fraction of content it shows.
            double contentHeight = view.Height / (viewSize / 100.0);
            double scrollableHeight = contentHeight - view.Height;
            if (scrollableHeight <= 0)
            {
                return;
            }

            bool fullyVisible = target.Top >= view.Top && target.Bottom <= view.Bottom;
            if (fullyVisible)
            {
                return;
            }

            double currentOffset = scrollableHeight * (vertical / 100.0);
            double elementTopInContent = target.Top - view.Top + currentOffset;

            // Anything taller than the viewport can only be shown from its top.
            double desiredOffset = target.Height + marginPixels >= view.Height
                ? elementTopInContent
                : elementTopInContent - marginPixels;

            desiredOffset = Math.Clamp(desiredOffset, 0, scrollableHeight);
            double desiredPercent = Math.Clamp(desiredOffset / scrollableHeight * 100.0, 0, 100);

            if (Math.Abs(desiredPercent - vertical) < 0.01)
            {
                return;
            }

            // UIA wants the NoScroll sentinel (-1) for an axis that cannot scroll; passing a real
            // percentage there fails the whole call.
            double horizontalArgument = pattern.HorizontallyScrollable.ValueOrDefault ? horizontal : NoScroll;
            pattern.SetScrollPercent(horizontalArgument, desiredPercent);
        }
    }

    private static void RequireScrollable(bool scrollable, string automationId, string axis)
    {
        if (!scrollable)
        {
            throw new DriverException("not-scrollable",
                $"Element '{automationId}' is not currently scrollable {axis}.", 2);
        }
    }
}