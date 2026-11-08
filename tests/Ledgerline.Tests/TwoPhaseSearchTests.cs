using System.Collections.Concurrent;
using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

/// <summary>The streaming logic on its own, over a fake search whose matches and timings each test controls.</summary>
public class TwoPhaseSearchTests
{
    private static readonly IndexLayout Layout = new(new DateOnly(2026, 11, 6));
    private static readonly string[] Order = ["hot", "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019"];

    [Fact]
    public async Task A_full_hot_page_is_sent_first_and_complete_then_totals_arrive_year_by_year()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        var events = await Run(fake, size: 50);

        Assert.Equal(Order, Assert.IsType<PlanEvent>(events[0]).Segments.Select(s => s.Label));
        var page = Assert.IsType<PageEvent>(events[1]);
        Assert.True(page.Complete);
        Assert.All(page.Hits, h => Assert.StartsWith("hot-", h.Id));
        Assert.Single(events.OfType<PageEvent>());

        Assert.Equal(Order, events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal(Enumerable.Range(1, 9), events.OfType<ProgressEvent>().Select(p => p.Searched));
        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(900, done.Totals.Count);
        Assert.Equal(900, done.Totals.ByStatus[PaymentStatus.Posted]);
    }

    [Fact]
    public async Task A_short_hot_page_is_topped_up_from_the_newest_cold_years_in_order()
    {
        var fake = new FakeSearch { Matches = new() { ["hot"] = 10, ["2026"] = 15, ["2025"] = 40, ["2024"] = 40 } };

        var events = await Run(fake, size: 50);

        var pages = events.OfType<PageEvent>().ToList();
        Assert.Equal([10, 25, 50], pages.Select(p => p.Hits.Count));
        Assert.Equal([false, false, true], pages.Select(p => p.Complete));
        Assert.Equal(
            [.. Ids("hot", 10), .. Ids("2026", 15), .. Ids("2025", 25)],
            pages[^1].Hits.Select(h => h.Id));
    }

    [Fact]
    public async Task Years_finishing_out_of_order_are_still_applied_newest_first()
    {
        // Older years answer first, as a small cold year might. The page must not take their hits early.
        var fake = new FakeSearch
        {
            Matches = new() { ["hot"] = 0, ["2026"] = 30, ["2025"] = 30, ["2024"] = 30 },
            DelayMs = new() { ["2026"] = 150, ["2025"] = 60, ["2024"] = 0 },
        };

        var events = await Run(fake, size: 50);

        Assert.Equal(Order, events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal([.. Ids("2026", 30), .. Ids("2025", 20)], events.OfType<PageEvent>().Last().Hits.Select(h => h.Id));
    }

    [Fact]
    public async Task Once_the_page_is_full_later_years_are_asked_for_totals_only()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        await Run(fake, size: 50, parallelism: 1);

        Assert.Equal(50, fake.Sizes["hot"]);
        Assert.All(Order.Skip(1), s => Assert.Equal(0, fake.Sizes[s]));
    }

    [Fact]
    public async Task Stopping_early_cancels_searches_in_flight_and_never_starts_the_rest()
    {
        var fake = new FakeSearch
        {
            Matches = Order.ToDictionary(s => s, _ => 100),
            DelayMs = Order.Skip(1).ToDictionary(s => s, _ => 5_000),
        };
        var search = new TwoPhaseSearch(fake, Layout, TimeProvider.System) { Parallelism = 3 };

        await foreach (var e in search.RunAsync(new PaymentQuery(), 50, TestContext.Current.CancellationToken))
        {
            if (e is PageEvent) break; // the person saw page 1 and closed the tab
        }

        Assert.Equal(["2024", "2025", "2026", "hot"], fake.Started.Order()); // hot, then three years in flight
        Assert.Equal(["2024", "2025", "2026"], fake.Cancelled.Order());
    }

    [Fact]
    public async Task A_range_outside_the_history_kept_ends_with_an_empty_complete_page()
    {
        var events = await Run(new FakeSearch(), 50, new PaymentQuery { From = new DateOnly(2010, 1, 1), To = new DateOnly(2011, 1, 1) });

        Assert.Empty(Assert.IsType<PlanEvent>(events[0]).Segments);
        Assert.Equal([], Assert.IsType<PageEvent>(events[1]).Hits);
        Assert.Equal(0, Assert.IsType<DoneEvent>(events[2]).Totals.Count);
    }

    [Fact]
    public async Task A_range_that_is_all_cold_skips_the_hot_tier()
    {
        var fake = new FakeSearch { Matches = new() { ["2024"] = 3, ["2023"] = 4 } };

        var events = await Run(fake, 50, new PaymentQuery { From = new DateOnly(2023, 6, 1), To = new DateOnly(2024, 3, 31) });

        Assert.Equal(["2024", "2023"], events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal(7, events.OfType<DoneEvent>().Single().Totals.Count);
    }

    private static async Task<List<SearchEvent>> Run(FakeSearch fake, int size, PaymentQuery? query = null, int parallelism = 3)
    {
        var search = new TwoPhaseSearch(fake, Layout, TimeProvider.System) { Parallelism = parallelism };
        var events = new List<SearchEvent>();
        await foreach (var e in search.RunAsync(query ?? new PaymentQuery(), size, TestContext.Current.CancellationToken))
        {
            events.Add(e);
        }
        return events;
    }

    private static IEnumerable<string> Ids(string segment, int count) => Enumerable.Range(0, count).Select(i => $"{segment}-{i:D3}");

    private sealed class FakeSearch : ISegmentSearch
    {
        public Dictionary<string, int> Matches { get; init; } = [];
        public Dictionary<string, int> DelayMs { get; init; } = [];
        public ConcurrentDictionary<string, int> Sizes { get; } = new();
        public ConcurrentBag<string> Started { get; } = [];
        public ConcurrentBag<string> Cancelled { get; } = [];

        public async Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default)
        {
            Started.Add(segment.Label);
            Sizes[segment.Label] = size;
            try
            {
                await Task.Delay(DelayMs.GetValueOrDefault(segment.Label), ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Add(segment.Label);
                throw;
            }

            var matches = Matches.GetValueOrDefault(segment.Label);
            return new SearchPage
            {
                Hits = Ids(segment.Label, Math.Min(size, matches)).Select(id => Payment(id, segment)).ToList(),
                Totals = new Totals(matches, matches, matches == 0 ? new Dictionary<PaymentStatus, long>() : new Dictionary<PaymentStatus, long> { [PaymentStatus.Posted] = matches }),
                TookMs = 1,
            };
        }

        private static Payment Payment(string id, Segment segment) => new()
        {
            Id = id, DepositDate = segment.To, Client = "c", Lockbox = "LB-1000", Payer = "p", CheckNumber = "1",
            Amount = 1, Invoices = ["INV-1"], Status = PaymentStatus.Posted, Memo = "",
        };
    }
}
