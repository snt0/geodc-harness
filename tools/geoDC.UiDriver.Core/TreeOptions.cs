namespace geoDC.UiDriver;

/// <summary>How much of the UIA tree <c>tree</c> walks and prints, and whether it prints a diff instead.</summary>
/// <param name="Interactable">
///     True for <c>--interactable</c>: print only the outline <see cref="UiaTreeFilter.KeepInteractable" /> keeps.
/// </param>
/// <param name="Depth">
///     The number of levels walked below the root, or null for no limit. Without <c>--depth</c> it is
///     <see cref="DefaultDepth" />, or no limit with <c>--interactable</c>. The limit applies to the walk, before
///     filtering, so it counts levels of the full tree.
/// </param>
/// <param name="Diff">
///     True for <c>--diff</c>: print only what changed since the snapshot stored by the last <c>tree</c> with the
///     same <see cref="TreeSnapshotKey" />; see <see cref="UiaTreeDiff" />.
/// </param>
/// <param name="Bounds">True for <c>--bounds</c>, which needs <c>--diff</c>: report bounding-rectangle changes too.</param>
public sealed record TreeOptions(bool Interactable, int? Depth, bool Diff = false, bool Bounds = false)
{
    /// <summary>The levels a plain <c>tree</c> walks below its root when no <c>--depth</c> is given.</summary>
    public const int DefaultDepth = 4;

    /// <summary>
    ///     Reads <c>tree</c>'s options; throws <c>bad-arguments</c> for a negative or non-integer depth, or for
    ///     <c>--bounds</c> without <c>--diff</c>.
    /// </summary>
    public static TreeOptions Parse(CliArgs cli)
    {
        bool interactable = cli.Has("--interactable");
        bool diff = cli.Has("--diff");
        bool bounds = cli.Has("--bounds");
        if (bounds && !diff)
        {
            throw new DriverException("bad-arguments",
                "--bounds applies only with --diff; a plain tree already prints every node's bounds.", 1);
        }

        if (!cli.Has("--depth"))
        {
            return new TreeOptions(interactable, interactable ? null : DefaultDepth, diff, bounds);
        }

        int depth = cli.Int("--depth", DefaultDepth);
        return depth >= 0
            ? new TreeOptions(interactable, depth, diff, bounds)
            : throw new DriverException("bad-arguments", $"Option '--depth' expects 0 or more but got {depth}.", 1);
    }
}