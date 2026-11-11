using System.Collections.Concurrent;
using System.Globalization;
using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

/// <summary>The streaming logic on its own, over a fake search whose matches and timings each test controls.</summary>
public class TwoPhaseSearchTests
{
    private static readonly IndexLayout Layout = new(new DateOnly(2026, 11, 6));
    private static readonly string[] Order = ["hot", "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019"];

    // Behaviour that must not depend on how cold history is searched.

    [Theory]
    [InlineData(ColdStrategy.Combined)]
    [InlineData(ColdStrategy.PerYear)]
    public async Task A_full_hot_page_is_sent_first_and_complete_then_totals_arrive_newest_year_first(ColdStrategy cold)
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        var events = await Run(fake, size: 50, cold);

        Assert.Equal(Order, Assert.IsType<PlanEvent>(events[0]).Segments.Select(s => s.Label));
        var page = Assert.IsType<PageEvent>(events[1]);
        Assert.True(page.Complete);
        Assert.All(page.Hits, h => Assert.StartsWith("hot-", h.Id));
        Assert.Single(events.OfType<PageEvent>());

        Assert.Equal(Order, events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal(Enumerable.Range(1, 9), events.OfType<ProgressEvent>().Select(p => p.Searched));
        Assert.All(events.OfType<ProgressEvent>(), p => Assert.Equal(100, p.SegmentTotals.Count));
        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(900, done.Totals.Count);
        Assert.Equal(900, done.Totals.ByStatus[PaymentStatus.Posted]);
    }

    [Theory]
    [InlineData(ColdStrategy.Combined, new[] { 10, 50 })]
    [InlineData(ColdStrategy.PerYear, new[] { 10, 25, 50 })]
    public async Task A_short_hot_page_is_topped_up_from_the_newest_cold_years_in_order(ColdStrategy cold, int[] pageSizes)
    {
        var fake = new FakeSearch { Matches = new() { ["hot"] = 10, ["2026"] = 15, ["2025"] = 40, ["2024"] = 40 } };

        var events = await Run(fake, size: 50, cold);

        var pages = events.OfType<PageEvent>().ToList();
        Assert.Equal(pageSizes, pages.Select(p => p.Hits.Count));
        Assert.True(pages[^1].Complete);
        Assert.Equal(
            [.. Ids("hot", 10), .. Ids("2026", 15), .. Ids("2025", 25)],
            pages[^1].Hits.Select(h => h.Id));
    }

    [Theory]
    [InlineData(ColdStrategy.Combined)]
    [InlineData(ColdStrategy.PerYear)]
    public async Task A_range_outside_the_history_kept_ends_with_an_empty_complete_page(ColdStrategy cold)
    {
        var events = await Run(new FakeSearch(), 50, cold, new PaymentQuery { From = new DateOnly(2010, 1, 1), To = new DateOnly(2011, 1, 1) });

        Assert.Empty(Assert.IsType<PlanEvent>(events[0]).Segments);
        Assert.Equal([], Assert.IsType<PageEvent>(events[1]).Hits);
        Assert.Equal(0, Assert.IsType<DoneEvent>(events[2]).Totals.Count);
    }

    [Theory]
    [InlineData(ColdStrategy.Combined)]
    [InlineData(ColdStrategy.PerYear)]
    public async Task A_range_that_is_all_cold_skips_the_hot_tier(ColdStrategy cold)
    {
        var fake = new FakeSearch { Matches = new() { ["2024"] = 3, ["2023"] = 4 } };

        var events = await Run(fake, 50, cold, new PaymentQuery { From = new DateOnly(2023, 6, 1), To = new DateOnly(2024, 3, 31) });

        Assert.Equal(["2024", "2023"], events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal([3, 4], events.OfType<ProgressEvent>().Select(p => (int)p.SegmentTotals.Count));
        Assert.Equal(7, events.OfType<DoneEvent>().Single().Totals.Count);
        Assert.DoesNotContain("hot", fake.Started);
    }

    [Theory]
    [InlineData(ColdStrategy.Combined)]
    [InlineData(ColdStrategy.PerYear)]
    public async Task A_year_with_no_matches_reports_zero_rather_than_disappearing(ColdStrategy cold)
    {
        var fake = new FakeSearch { Matches = new() { ["hot"] = 1, ["2026"] = 2, ["2024"] = 3 } };

        var events = await Run(fake, 50, cold);

        var progress = events.OfType<ProgressEvent>().ToDictionary(p => p.Segment, p => p.SegmentTotals.Count);
        Assert.Equal(Order, progress.Keys);
        Assert.Equal(0, progress["2025"]);
        Assert.Equal(6, events.OfType<DoneEvent>().Single().Totals.Count);
    }

    // Combined: two requests per search.

    [Fact]
    public async Task Combined_makes_one_hot_and_one_cold_request_side_by_side()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        await Run(fake, 50, ColdStrategy.Combined);

        Assert.Equal(["cold", "hot"], fake.Started.Order());
    }

    [Fact]
    public async Task Combined_counts_cold_history_without_fetching_a_page_it_does_not_need()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        await Run(fake, 50, ColdStrategy.Combined);

        Assert.Equal(0, fake.Sizes["cold"]);
        Assert.DoesNotContain("cold-page", fake.Started);
    }

    [Fact]
    public async Task Combined_tops_up_a_short_hot_page_with_one_page_only_request_for_what_is_missing()
    {
        var fake = new FakeSearch { Matches = new() { ["hot"] = 10, ["2026"] = 15, ["2025"] = 40 } };

        await Run(fake, 50, ColdStrategy.Combined);

        Assert.Equal(["cold", "cold-page", "hot"], fake.Started.Order());
        Assert.Equal(40, fake.Sizes["cold-page"]);
    }

    [Fact]
    public async Task Combined_sends_the_hot_page_without_waiting_for_cold_history()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100), DelayMs = new() { ["cold"] = 300 } };
        var search = new TwoPhaseSearch(fake, Layout, TimeProvider.System) { Cold = ColdStrategy.Combined };
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var e in search.RunAsync(new PaymentQuery(), 50, TestContext.Current.CancellationToken))
        {
            if (e is PageEvent page)
            {
                Assert.True(page.Complete);
                Assert.True(clock.ElapsedMilliseconds < 250, $"page took {clock.ElapsedMilliseconds} ms");
                break;
            }
        }

        Assert.Equal(["cold"], fake.Cancelled);
    }

    // PerYear: one request per year, a few at a time.

    [Fact]
    public async Task PerYear_applies_years_newest_first_even_when_they_finish_out_of_order()
    {
        // Older years answer first, as a small cold year might. The page must not take their hits early.
        var fake = new FakeSearch
        {
            Matches = new() { ["hot"] = 0, ["2026"] = 30, ["2025"] = 30, ["2024"] = 30 },
            DelayMs = new() { ["2026"] = 150, ["2025"] = 60, ["2024"] = 0 },
        };

        var events = await Run(fake, size: 50, ColdStrategy.PerYear);

        Assert.Equal(Order, events.OfType<ProgressEvent>().Select(p => p.Segment));
        Assert.Equal([.. Ids("2026", 30), .. Ids("2025", 20)], events.OfType<PageEvent>().Last().Hits.Select(h => h.Id));
    }

    [Fact]
    public async Task PerYear_asks_later_years_for_totals_only_once_the_page_is_full()
    {
        var fake = new FakeSearch { Matches = Order.ToDictionary(s => s, _ => 100) };

        await Run(fake, size: 50, ColdStrategy.PerYear, parallelism: 1);

        Assert.Equal(50, fake.Sizes["hot"]);
        Assert.All(Order.Skip(1), s => Assert.Equal(0, fake.Sizes[s]));
    }

    [Fact]
    public async Task PerYear_stopping_early_cancels_searches_in_flight_and_never_starts_the_rest()
    {
        var fake = new FakeSearch
        {
            Matches = Order.ToDictionary(s => s, _ => 100),
            DelayMs = Order.Skip(1).ToDictionary(s => s, _ => 5_000),
        };
        var search = new TwoPhaseSearch(fake, Layout, TimeProvider.System) { Parallelism = 3, Cold = ColdStrategy.PerYear };

        await foreach (var e in search.RunAsync(new PaymentQuery(), 50, TestContext.Current.CancellationToken))
        {
            if (e is PageEvent) break; // the person saw page 1 and closed the tab
        }

        Assert.Equal(["2024", "2025", "2026", "hot"], fake.Started.Order()); // hot, then three years in flight
        Assert.Equal(["2024", "2025", "2026"], fake.Cancelled.Order());
    }

    private static async Task<List<SearchEvent>> Run(
        FakeSearch fake, int size, ColdStrategy cold, PaymentQuery? query = null, int parallelism = 3)
    {
        var search = new TwoPhaseSearch(fake, Layout, TimeProvider.System) { Parallelism = parallelism, Cold = cold };
        var events = new List<SearchEvent>();
        await foreach (var e in search.RunAsync(query ?? new PaymentQuery(), size, TestContext.Current.CancellationToken))
        {
            events.Add(e);
        }
        return events;
    }

    private static IEnumerable<string> Ids(string segment, int count) => Enumerable.Range(0, count).Select(i => $"{segment}-{i:D3}");

    /// <summary>Matches and delays per segment label ("hot", "2026", …); the combined cold request is labelled "cold".</summary>
    private sealed class FakeSearch : ISegmentSearch
    {
        public Dictionary<string, int> Matches { get; init; } = [];
        public Dictionary<string, int> DelayMs { get; init; } = [];
        public ConcurrentDictionary<string, int> Sizes { get; } = new();
        public ConcurrentBag<string> Started { get; } = [];
        public ConcurrentBag<string> Cancelled { get; } = [];

        public async Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default)
        {
            await Begin(segment.Label, size, ct);
            return Page(segment.Label, segment.To, size);
        }

        public async Task<YearSearch> SearchByYearAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default)
        {
            await Begin(segment.Label, size, ct);
            var years = Enumerable.Range(segment.From.Year, segment.To.Year - segment.From.Year + 1).Reverse()
                .Select(y => (Year: y, Page: Page(y.ToString(CultureInfo.InvariantCulture), new DateOnly(y, 12, 31), size)))
                .ToList();
            var all = years.Aggregate(Totals.Empty, (t, y) => t.Add(y.Page.Totals));
            return new YearSearch(
                new SearchPage { Hits = years.SelectMany(y => y.Page.Hits).Take(size).ToList(), Totals = all, TookMs = 1 },
                years.Where(y => y.Page.Totals.Count > 0).ToDictionary(y => y.Year, y => y.Page.Totals)); // like min_doc_count 1
        }

        public async Task<IReadOnlyList<Payment>> PageAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default)
        {
            await Begin($"{segment.Label}-page", size, ct);
            return Enumerable.Range(segment.From.Year, segment.To.Year - segment.From.Year + 1).Reverse()
                .SelectMany(y => Page(y.ToString(CultureInfo.InvariantCulture), new DateOnly(y, 12, 31), size).Hits)
                .Take(size)
                .ToList();
        }

        private async Task Begin(string label, int size, CancellationToken ct)
        {
            Started.Add(label);
            Sizes[label] = size;
            try
            {
                await Task.Delay(DelayMs.GetValueOrDefault(label), ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Add(label);
                throw;
            }
        }

        private SearchPage Page(string label, DateOnly date, int size)
        {
            var matches = Matches.GetValueOrDefault(label);
            return new SearchPage
            {
                Hits = Ids(label, Math.Min(size, matches)).Select(id => Payment(id, date)).ToList(),
                Totals = new Totals(matches, matches, matches == 0
                    ? new Dictionary<PaymentStatus, long>()
                    : new Dictionary<PaymentStatus, long> { [PaymentStatus.Posted] = matches }),
                TookMs = 1,
            };
        }

        private static Payment Payment(string id, DateOnly date) => new()
        {
            Id = id, DepositDate = date, Client = "c", Lockbox = "LB-1000", Payer = "p", CheckNumber = "1",
            Amount = 1, Invoices = ["INV-1"], Status = PaymentStatus.Posted, Memo = "",
        };
    }
}
