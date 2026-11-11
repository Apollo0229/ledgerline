using System.Globalization;
using System.Runtime.CompilerServices;
using Ledgerline.Core;

namespace Ledgerline.Search;

/// <summary>Searches segments of history. <see cref="PaymentSearch"/> is the real one; tests substitute fakes.</summary>
public interface ISegmentSearch
{
    Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default);

    /// <summary>One request over several years, with totals broken down by calendar year.</summary>
    Task<YearSearch> SearchByYearAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default);

    /// <summary>Only the newest matches, without totals, which is far cheaper than a search with them.</summary>
    Task<IReadOnlyList<Payment>> PageAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default);
}

/// <summary>How cold history is searched once the hot tier has answered.</summary>
public enum ColdStrategy
{
    /// <summary>
    /// One request that counts all cold years, broken down by year, alongside the hot tier; a third, cheap
    /// page-only request only when the hot tier is short of a page. It costs the cluster about what one
    /// plain query with the same per-year breakdown does. The default: see the measurements in the README.
    /// </summary>
    Combined,

    /// <summary>
    /// One request per year, three at a time, so totals arrive year by year. Nicer to watch on a huge
    /// history, but it multiplies the requests per search and halves throughput under load.
    /// </summary>
    PerYear,
}

/// <summary>
/// Answers a search in stages instead of all at once.
/// <list type="number">
/// <item>The hot tier first. Results are sorted newest first, so if it holds a full page, that page is
/// already the exact first page of the whole answer and is sent immediately.</item>
/// <item>Then cold history (see <see cref="ColdStrategy"/>), with totals per year newest first, topping up
/// the page if the hot tier had too few matches.</item>
/// </list>
/// Results are applied strictly in order, so the page stays exact. When the caller stops listening (the
/// browser tab closes, the query changes) searches in flight are cancelled and the rest never start.
/// </summary>
public sealed class TwoPhaseSearch(ISegmentSearch search, IndexLayout layout, TimeProvider time)
{
    /// <summary>With <see cref="ColdStrategy.PerYear"/>, how many years may be searched at once.</summary>
    public int Parallelism { get; init; } = 3;

    public ColdStrategy Cold { get; init; } = ColdStrategy.Combined;

    public IAsyncEnumerable<SearchEvent> RunAsync(PaymentQuery query, int size, CancellationToken ct = default) =>
        RunAsync(query, size, Cold, ct);

    public async IAsyncEnumerable<SearchEvent> RunAsync(
        PaymentQuery query, int size, ColdStrategy cold, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var started = time.GetTimestamp();
        long Now() => (long)time.GetElapsedTime(started).TotalMilliseconds;

        var hotSegment = layout.Hot(query.From, query.To);
        var years = layout.ColdYears(query.From, query.To);
        var segments = new List<Segment>();
        if (hotSegment is not null) segments.Add(hotSegment);
        segments.AddRange(years);

        yield return new PlanEvent(Now(), [.. segments.Select(x => new PlannedSegment(x.Label, x.From, x.To))]);
        if (segments.Count == 0)
        {
            yield return new PageEvent(Now(), [], Complete: true);
            yield return new DoneEvent(Now(), Totals.Empty, FirstPageMs: Now());
            yield break;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var hits = new List<Payment>();

        // Every planned segment gets a task for its result, started in plan order up to a limit. Once the
        // page is full, later per-year searches only need totals.
        Func<int, Task<SearchPage>> start;
        int limit;
        if (cold == ColdStrategy.PerYear)
        {
            start = i => search.SearchAsync(query, segments[i], hits.Count >= size ? 0 : size, cts.Token);
            limit = Parallelism;
        }
        else
        {
            // The hot tier and the counts for all cold years run side by side; each year's result is read from
            // the one cold response. The cold side fetches no page: the hot tier usually fills it, and when it
            // does not, the newest year asks a page-only request for exactly what is missing.
            var hotTask = hotSegment is null ? null : search.SearchAsync(query, hotSegment, size, cts.Token);
            var coldAll = years.Count == 0 ? null : new Segment(
                "cold", years[^1].From, years[0].To, [.. years.SelectMany(y => y.Indices)]);
            var coldTask = coldAll is null ? null : search.SearchByYearAsync(query, coldAll, 0, cts.Token);
            start = i =>
            {
                var segment = segments[i];
                if (segment == hotSegment) return hotTask!;
                var year = int.Parse(segment.Label, CultureInfo.InvariantCulture);
                var page = segment == years[0] ? TopUp(hotTask, query, coldAll!, size, cts.Token) : null;
                return YearOf(coldTask!, year, page);
            };
            limit = segments.Count;
        }

        var inFlight = new Queue<Task<SearchPage>>();
        var launched = 0;
        void Launch()
        {
            while (inFlight.Count < limit && launched < segments.Count) inFlight.Enqueue(start(launched++));
        }

        var running = Totals.Empty;
        var (sentCount, sentComplete) = (-1, false);
        long? firstPageMs = null;
        try
        {
            Launch();
            for (var i = 0; i < segments.Count; i++)
            {
                var result = await inFlight.Dequeue().ConfigureAwait(false);
                if (hits.Count < size) hits.AddRange(result.Hits.Take(size - hits.Count));
                Launch();

                running = running.Add(result.Totals);
                var complete = hits.Count >= size || i == segments.Count - 1;
                if ((hits.Count != sentCount || complete != sentComplete) && (hits.Count > 0 || complete))
                {
                    (sentCount, sentComplete) = (hits.Count, complete);
                    firstPageMs ??= Now();
                    yield return new PageEvent(Now(), [.. hits], complete);
                }

                var segment = segments[i];
                yield return new ProgressEvent(
                    Now(), segment.Label, segment.From, segment.To, result.Totals, running, i + 1, segments.Count);
            }

            yield return new DoneEvent(Now(), running, firstPageMs);
        }
        finally
        {
            // Reached early when the caller stops listening: stop what is still running.
            // Their outcome no longer matters, so wait for them without letting a failure mask the original exit.
            await cts.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(inFlight.Cast<Task>()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>The cold matches needed to finish the page: none if the hot tier already filled it.</summary>
    private async Task<IReadOnlyList<Payment>> TopUp(Task<SearchPage>? hot, PaymentQuery query, Segment cold, int size, CancellationToken ct)
    {
        var have = hot is null ? 0 : (await hot.ConfigureAwait(false)).Hits.Count;
        return have >= size ? [] : await search.PageAsync(query, cold, size - have, ct).ConfigureAwait(false);
    }

    /// <summary>One year's share of the combined cold search: its totals, plus the page top-up if it is the newest year.</summary>
    private static async Task<SearchPage> YearOf(Task<YearSearch> cold, int year, Task<IReadOnlyList<Payment>>? page)
    {
        var all = await cold.ConfigureAwait(false);
        return new SearchPage
        {
            Hits = page is null ? [] : await page.ConfigureAwait(false),
            Totals = all.ByYear.GetValueOrDefault(year, Totals.Empty),
            TookMs = all.Page.TookMs,
        };
    }
}
