namespace geoDC.UiDriver;

/// <summary>
///     Diagnostic fields added to an element-resolution error, emitted beside <c>error.code</c> and
///     <c>error.candidates</c>. Every field is optional in the JSON: an empty list or a null hint is left out.
/// </summary>
/// <param name="Closest">
///     Up to <see cref="ElementLookupAnalyzer.ClosestLimit" /> AutomationIds from the searched roots that resemble
///     the requested one, closest first.
/// </param>
/// <param name="Elsewhere">
///     <c>--window</c> selectors of the windows where the requested AutomationId exists outside the searched roots
///     (outside <c>--scope</c>, or outside the main window under <c>--main-window</c>).
/// </param>
/// <param name="Matches">
///     For an ambiguous AutomationId, the first <see cref="ElementLookupAnalyzer.MatchLimit" /> matches, each with the
///     ancestor that can scope to it.
/// </param>
/// <param name="Hint">One suggested next step, phrased as UI driver options the caller can use as given.</param>
public sealed record ElementLookupHints(
    IReadOnlyList<string> Closest,
    IReadOnlyList<string> Elsewhere,
    IReadOnlyList<ScopedMatch> Matches,
    string? Hint)
{
    /// <summary>No fields set; a starting point for hints that carry only a <see cref="Hint" />.</summary>
    public static ElementLookupHints Empty { get; } = new([], [], [], null);
}

/// <summary>One element an ambiguous AutomationId matched.</summary>
/// <param name="Element">The element as the UI driver describes it: id, control type, and ancestor chain.</param>
/// <param name="Scope">
///     The innermost ancestor AutomationId that contains this match and no other, and that is itself unique, so
///     <c>--scope</c> resolves to it; null when no ancestor qualifies.
/// </param>
public sealed record ScopedMatch(string Element, string? Scope);

/// <summary>An ambiguous match as read from the UIA tree.</summary>
/// <param name="Element">The element as the UI driver describes it.</param>
/// <param name="AncestorIds">The AutomationIds of the element's ancestors, innermost first.</param>
public sealed record MatchAncestry(string Element, IReadOnlyList<string> AncestorIds);

/// <summary>What a failed lookup saw, read from the UIA tree once the resolution timed out.</summary>
/// <param name="RequestedId">The AutomationId that was not found.</param>
/// <param name="Option">The option that named it: <c>--id</c>, or <c>--scope</c> when the scope anchor is missing.</param>
/// <param name="ScopeId">The <c>--scope</c> the search was narrowed to, if any.</param>
/// <param name="MainWindowOnly">Whether <c>--main-window</c> narrowed the search.</param>
/// <param name="SearchedIds">Every non-empty AutomationId in the searched roots.</param>
/// <param name="Elsewhere">
///     <c>--window</c> selectors of the windows holding the requested AutomationId outside the searched roots; empty
///     when the search was not narrowed or the id exists nowhere.
/// </param>
/// <param name="ScrollContainerIds">
///     AutomationIds of scrollable containers in the searched roots, list-like containers first: where a
///     virtualized, never-realized item would be hiding.
/// </param>
public sealed record ElementNotFoundFacts(
    string RequestedId,
    string Option,
    string? ScopeId,
    bool MainWindowOnly,
    IReadOnlyCollection<string> SearchedIds,
    IReadOnlyList<string> Elsewhere,
    IReadOnlyList<string> ScrollContainerIds);

/// <summary>A searched AutomationId scored against the requested one.</summary>
/// <param name="Id">The candidate AutomationId.</param>
/// <param name="Distance">Case-insensitive Levenshtein distance to the requested id.</param>
/// <param name="Overlap">
///     Characters shared as a common prefix plus a common suffix (case-insensitive), or the shorter length when one
///     id contains the other.
/// </param>
/// <param name="IsTypo">True when <see cref="Distance" /> is small enough to read as a misspelling.</param>
public sealed record ClosestId(string Id, int Distance, int Overlap, bool IsTypo);

/// <summary>
///     Turns what a failed element lookup saw into the <see cref="ElementLookupHints" /> of its error. Pure: the
///     UIA reads happen in <c>ElementResolver</c>, which hands the results over as plain values.
/// </summary>
public static class ElementLookupAnalyzer
{
    /// <summary>The most AutomationIds <see cref="ElementLookupHints.Closest" /> carries.</summary>
    public const int ClosestLimit = 5;

    /// <summary>The most ambiguous matches <see cref="ElementLookupHints.Matches" /> carries.</summary>
    public const int MatchLimit = 10;

    /// <summary>Overlap below this is coincidence between short ids, not resemblance.</summary>
    private const int MinimumOverlap = 3;

    /// <summary>
    ///     Ranks <paramref name="searchedIds" /> by resemblance to <paramref name="requestedId" /> and keeps at most
    ///     <paramref name="limit" />. An id resembles the request when its edit distance is small enough to be a typo,
    ///     or when a shared prefix and suffix (or containment) covers at least two thirds of the request. A shared
    ///     role suffix such as <c>Button</c> alone is not enough. Typos rank first, by distance. The rest rank by
    ///     overlap, then distance. Ties break by ordinal id, so the order is stable. The requested id itself is not
    ///     returned.
    /// </summary>
    public static IReadOnlyList<ClosestId> RankClosest(string requestedId, IEnumerable<string> searchedIds,
        int limit = ClosestLimit)
    {
        string requested = requestedId.ToLowerInvariant();
        int typoDistance = Math.Clamp(requested.Length / 4, 1, 3);
        List<ClosestId> related = [];

        foreach (string id in searchedIds.Where(id => id.Length > 0).Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(id, requestedId, StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = id.ToLowerInvariant();
            int distance = Levenshtein(requested, candidate);
            int overlap = Overlap(requested, candidate);
            bool isTypo = distance <= typoDistance;

            if (isTypo || (overlap >= MinimumOverlap && overlap * 3 >= requested.Length * 2))
            {
                related.Add(new ClosestId(id, distance, overlap, isTypo));
            }
        }

        return
        [
            .. related
                .OrderByDescending(entry => entry.IsTypo)
                .ThenBy(entry => entry.IsTypo ? entry.Distance : 0)
                .ThenByDescending(entry => entry.Overlap)
                .ThenBy(entry => entry.Distance)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .Take(limit)
        ];
    }

    /// <summary>
    ///     Hints for an AutomationId that resolved to nothing. The hint prefers, in order: the id existing outside
    ///     a narrowed search, a typo-close id, a scroll container that may hold it virtualized, any resembling id,
    ///     and otherwise a pointer to <c>tree</c>.
    /// </summary>
    public static ElementLookupHints NotFound(ElementNotFoundFacts facts)
    {
        IReadOnlyList<ClosestId> ranked = RankClosest(facts.RequestedId, facts.SearchedIds);
        IReadOnlyList<string> closest = [.. ranked.Select(entry => entry.Id)];
        return new ElementLookupHints(closest, facts.Elsewhere, [], ChooseNotFoundHint(facts, ranked));
    }

    /// <summary>
    ///     Hints for an AutomationId that matched several elements: each match with its scope, and a hint naming
    ///     the first scope found, or saying that no ancestor separates the matches.
    /// </summary>
    public static ElementLookupHints Ambiguous(IReadOnlyList<MatchAncestry> matches, Func<string, bool> isUniqueId)
    {
        IReadOnlyList<ScopedMatch> scoped = ScopeMatches(matches, isUniqueId);
        string? firstScope = scoped.Select(match => match.Scope).FirstOrDefault(scope => scope is not null);
        string hint = firstScope is null
            ? "no unique ancestor AutomationId separates the matches; give their containers distinct AutomationIds " +
              "to scope to"
            : $"use --scope {firstScope} to select the match it contains; each entry in matches names the scope " +
              "that selects it";
        return new ElementLookupHints([], [], scoped, hint);
    }

    /// <summary>
    ///     Pairs each of the first <paramref name="limit" /> matches with the innermost ancestor id that no other
    ///     match (of all of them) has as an ancestor and that <paramref name="isUniqueId" /> confirms exists exactly
    ///     once, so <c>--scope</c> resolves to it and it contains this match alone. <paramref name="isUniqueId" /> is
    ///     called only for ids that already separate a match, innermost first, and stops at the first that passes.
    /// </summary>
    public static IReadOnlyList<ScopedMatch> ScopeMatches(IReadOnlyList<MatchAncestry> matches,
        Func<string, bool> isUniqueId, int limit = MatchLimit)
    {
        Dictionary<string, int> holders = new(StringComparer.Ordinal);
        foreach (MatchAncestry match in matches)
        {
            foreach (string id in match.AncestorIds.Distinct(StringComparer.Ordinal))
            {
                holders[id] = holders.GetValueOrDefault(id) + 1;
            }
        }

        List<ScopedMatch> scoped = [];
        foreach (MatchAncestry match in matches.Take(limit))
        {
            string? scope = match.AncestorIds
                .Where(id => id.Length > 0 && holders[id] == 1)
                .FirstOrDefault(isUniqueId);
            scoped.Add(new ScopedMatch(match.Element, scope));
        }

        return scoped;
    }

    /// <summary>
    ///     The <c>--window</c> selector that names a window: <c>main</c> for the main window,
    ///     <c>title:&lt;title&gt;</c> when no other title contains the window's title (selectors match title
    ///     substrings, case-insensitively), and otherwise <c>handle:0x…</c>.
    /// </summary>
    /// <param name="title">The window's title; may be empty.</param>
    /// <param name="handle">The window's native handle.</param>
    /// <param name="isMain">Whether this is the main window of the driver's instance.</param>
    /// <param name="allTitles">The titles of every top-level window, this one included.</param>
    public static string WindowSelector(string title, long handle, bool isMain, IReadOnlyList<string> allTitles)
    {
        if (isMain)
        {
            return "main";
        }

        if (title.Length > 0 &&
            allTitles.Count(other => other.Contains(title, StringComparison.OrdinalIgnoreCase)) <= 1)
        {
            return $"title:{title}";
        }

        return $"handle:0x{handle:X8}";
    }

    /// <summary>
    ///     Hint for an element that exists but was never laid out. For an offscreen element inside a scroll container,
    ///     the hint suggests <c>scrollto</c>, and <c>scroll</c> on the container when it has an AutomationId. For one
    ///     outside any scroll container, it suggests revealing what hides it. For a zero-sized element, it points at
    ///     a collapsed or hidden parent.
    /// </summary>
    /// <param name="automationId">The element's AutomationId.</param>
    /// <param name="offscreen">The element's UIA IsOffscreen.</param>
    /// <param name="insideScrollContainer">Whether a scrollable ancestor encloses the element.</param>
    /// <param name="scrollContainerId">That ancestor's AutomationId; null when it has none.</param>
    /// <param name="scopeId">
    ///     The <c>--scope</c> the element was resolved under, repeated in the suggested <c>scrollto</c> so it resolves
    ///     the same element; null when none was given.
    /// </param>
    public static string NotReadyHint(string automationId, bool offscreen, bool insideScrollContainer,
        string? scrollContainerId, string? scopeId = null)
    {
        if (!offscreen)
        {
            return $"'{automationId}' has zero size; it or a parent is likely collapsed or hidden";
        }

        if (!insideScrollContainer)
        {
            return $"'{automationId}' is offscreen outside any scroll container; it may sit on an unselected tab, " +
                   "a collapsed expander, or a hidden panel; reveal that first";
        }

        string scrollTo = scopeId is null
            ? $"scrollto --id {automationId}"
            : $"scrollto --id {automationId} --scope {scopeId}";
        return scrollContainerId is null
            ? $"'{automationId}' is offscreen inside a scroll container; run {scrollTo} and retry"
            : $"'{automationId}' is offscreen inside {scrollContainerId}; run {scrollTo} " +
              $"(or scroll --id {scrollContainerId}) and retry";
    }

    /// <summary>Ordinal edit distance between two strings; lower-case both first for a case-insensitive one.</summary>
    public static int Levenshtein(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string? ChooseNotFoundHint(ElementNotFoundFacts facts, IReadOnlyList<ClosestId> ranked)
    {
        if (facts.Elsewhere.Count > 0)
        {
            List<string> narrowing = [];
            if (facts.ScopeId is not null)
            {
                narrowing.Add($"--scope {facts.ScopeId}");
            }

            if (facts.MainWindowOnly)
            {
                narrowing.Add("--main-window");
            }

            string window = QuoteIfSpaced(facts.Elsewhere[0]);
            return $"drop {string.Join(" and ", narrowing)}: '{facts.RequestedId}' exists outside the searched " +
                   $"{(facts.ScopeId is null ? "main window" : "scope")}, in --window {window} " +
                   $"(tree --window {window} shows it)";
        }

        if (ranked.Count > 0 && ranked[0].IsTypo)
        {
            return $"use {facts.Option} {ranked[0].Id}";
        }

        if (facts.ScrollContainerIds.Count > 0)
        {
            string container = facts.ScrollContainerIds[0];
            return $"try scroll --id {container} (or scrollto a nearby element) and retry; " +
                   $"'{facts.RequestedId}' may be an unrealized item in a virtualized list";
        }

        if (ranked.Count > 0)
        {
            return $"use {facts.Option} {ranked[0].Id}, or run tree --depth 4 to list the AutomationIds that exist";
        }

        return facts.ScopeId is null
            ? $"'{facts.RequestedId}' is in no open window; open the page or popup that holds it, or run " +
              "tree --depth 4 to list the AutomationIds that exist"
            : $"run tree --id {facts.ScopeId} to list the AutomationIds under the scope";
    }

    private static int Overlap(string requested, string candidate)
    {
        int shorter = Math.Min(requested.Length, candidate.Length);
        if (candidate.Contains(requested, StringComparison.Ordinal) ||
            requested.Contains(candidate, StringComparison.Ordinal))
        {
            return shorter;
        }

        int prefix = 0;
        while (prefix < shorter && requested[prefix] == candidate[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < shorter - prefix &&
               requested[requested.Length - 1 - suffix] == candidate[candidate.Length - 1 - suffix])
        {
            suffix++;
        }

        return prefix + suffix;
    }

    private static string QuoteIfSpaced(string selector)
    {
        return selector.Any(char.IsWhiteSpace) ? $"\"{selector}\"" : selector;
    }
}