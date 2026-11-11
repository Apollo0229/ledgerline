using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

/// <summary>Streamed answers must equal the plain one-query answer: same page, same totals.</summary>
[Collection(nameof(ElasticsearchTests))]
public class TwoPhaseIntegrationTests(SearchFixture fx)
{
    private static readonly (string Because, PaymentQuery Query)[] Cases =
    [
        ("everything: the hot tier fills the page", new PaymentQuery()),
        ("rare: the page needs cold years", new PaymentQuery { Status = PaymentStatus.Returned, MinAmount = 2_000 }),
        ("all cold: no hot tier at all", new PaymentQuery { From = new DateOnly(2021, 5, 1), To = new DateOnly(2023, 2, 28) }),
        ("free text", new PaymentQuery { Text = "Supply" }),
        ("nothing matches", new PaymentQuery { MinAmount = 999_999 }),
    ];

    public static TheoryData<string, PaymentQuery, ColdStrategy> Queries
    {
        get
        {
            var data = new TheoryData<string, PaymentQuery, ColdStrategy>();
            foreach (var (because, query) in Cases)
            foreach (var cold in Enum.GetValues<ColdStrategy>())
            {
                data.Add(because, query, cold);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Streamed_search_matches_the_single_query_baseline(string because, PaymentQuery query, ColdStrategy cold)
    {
        fx.RequireCluster();
        var ct = TestContext.Current.CancellationToken;
        var search = new PaymentSearch(fx.Client);
        var baseline = await search.SearchAsync(query, fx.Layout.All(query.From, query.To)!, 50, ct);

        var events = new List<SearchEvent>();
        await foreach (var e in new TwoPhaseSearch(search, fx.Layout, TimeProvider.System).RunAsync(query, 50, cold, ct))
        {
            events.Add(e);
        }

        var page = events.OfType<PageEvent>().Last();
        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.True(page.Complete, because);
        Assert.Equal(baseline.Hits.Select(h => h.Id), page.Hits.Select(h => h.Id));
        Assert.Equal(baseline.Totals.Count, done.Totals.Count);
        Assert.InRange(done.Totals.Amount - baseline.Totals.Amount, -0.05m, 0.05m);
        Assert.Equal(baseline.Totals.ByStatus, done.Totals.ByStatus);

        // Each year's own totals must match a plain search over just that year.
        foreach (var p in events.OfType<ProgressEvent>().Where(p => p.Segment != "hot"))
        {
            var year = await search.SearchAsync(query, fx.Layout.All(p.From, p.To)!, 0, ct);
            Assert.Equal(year.Totals.Count, p.SegmentTotals.Count);
            Assert.Equal(year.Totals.ByStatus, p.SegmentTotals.ByStatus);
        }
    }
}
