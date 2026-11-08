using System.Runtime.CompilerServices;
using Ledgerline.Core;

namespace Ledgerline.Search;

/// <summary>Searches one segment of history. <see cref="PaymentSearch"/> is the real one; tests substitute fakes.</summary>
public interface ISegmentSearch
{
    Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default);
}

/// <summary>
/// Answers a search in stages instead of all at once.
/// <list type="number">
/// <item>The hot tier first. Results are sorted newest first, so if it holds a full page, that page is
/// already the exact first page of the whole answer and is sent immediately.</item>
/// <item>Then cold history a year at a time, newest first, sending running totals after each year and
/// topping up the page if the hot tier had too few matches.</item>
/// </list>
/// Years are searched a few at a time but applied strictly in order, so the page stays exact. When the
/// caller stops listening (the browser tab closes, the query changes) the searches still in flight are
/// cancelled and the rest are never started.
/// </summary>
public sealed class TwoPhaseSearch(ISegmentSearch search, IndexLayout layout, TimeProvider time)
{
    /// <summary>How many segments may be searched at once.</summary>
    public int Parallelism { get; init; } = 3;

    public async IAsyncEnumerable<SearchEvent> RunAsync(
        PaymentQuery query, int size, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var started = time.GetTimestamp();
        long Now() => (long)time.GetElapsedTime(started).TotalMilliseconds;

        var segments = new List<Segment>();
        if (layout.Hot(query.From, query.To) is { } hot) segments.Add(hot);
        segments.AddRange(layout.ColdYears(query.From, query.To));

        if (segments.Count == 0)
        {
            yield return new PageEvent(Now(), [], Complete: true);
            yield return new DoneEvent(Now(), Totals.Empty, FirstPageMs: Now());
            yield break;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var inFlight = new Queue<Task<SearchPage>>();
        var launched = 0;
        var hits = new List<Payment>();

        // Start segments up to the parallelism limit. Once the page is full, later segments only need totals.
        void Launch()
        {
            while (inFlight.Count < Parallelism && launched < segments.Count)
            {
                var pageSize = hits.Count >= size ? 0 : size;
                inFlight.Enqueue(search.SearchAsync(query, segments[launched++], pageSize, cts.Token));
            }
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
}
