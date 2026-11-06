using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

[Collection(nameof(ElasticsearchTests))]
public class PaymentSearchTests(SearchFixture fx)
{
    private readonly PaymentSearch _search = new(fx.Client);

    [Fact]
    public async Task Unfiltered_search_finds_every_payment_newest_first()
    {
        fx.RequireCluster();

        var page = await _search.SearchAsync(new PaymentQuery(), fx.Layout.All(null, null)!, 50, TestContext.Current.CancellationToken);

        AssertMatches(fx.Payments, page);
        Assert.Equal(SearchFixture.Count, page.Totals.Count);
    }

    [Fact]
    public async Task Filters_match_the_same_payments_as_linq()
    {
        fx.RequireCluster();
        var client = fx.Payments.GroupBy(p => p.Client).OrderByDescending(g => g.Count()).First().Key;
        var query = new PaymentQuery
        {
            Client = client,
            Status = PaymentStatus.Posted,
            MinAmount = 1_000,
            MaxAmount = 25_000,
            From = new DateOnly(2022, 3, 1),
            To = new DateOnly(2025, 9, 30),
        };

        var page = await _search.SearchAsync(query, fx.Layout.All(query.From, query.To)!, 50, TestContext.Current.CancellationToken);

        var expected = fx.Payments.Where(p =>
            p.Client == client && p.Status == PaymentStatus.Posted &&
            p.Amount is >= 1_000 and <= 25_000 &&
            p.DepositDate >= query.From && p.DepositDate <= query.To);
        AssertMatches(expected, page);
        Assert.InRange(page.Totals.Count, 50, SearchFixture.Count / 2); // a real filter, not everything
    }

    [Fact]
    public async Task Text_finds_a_payment_by_invoice_number()
    {
        fx.RequireCluster();
        var target = fx.Payments[1234];

        var page = await _search.SearchAsync(
            new PaymentQuery { Text = target.Invoices[0] }, fx.Layout.All(null, null)!, 50, TestContext.Current.CancellationToken);

        Assert.Contains(page.Hits, p => p.Id == target.Id);
        AssertMatches(fx.Payments.Where(p => p.Invoices.Contains(target.Invoices[0])), page);
    }

    [Fact]
    public async Task Hot_and_cold_years_add_up_to_the_whole_range()
    {
        fx.RequireCluster();
        var query = new PaymentQuery { Status = PaymentStatus.Exception };
        var ct = TestContext.Current.CancellationToken;

        var all = await _search.SearchAsync(query, fx.Layout.All(null, null)!, 0, ct);
        var sum = (await _search.SearchAsync(query, fx.Layout.Hot(null, null)!, 0, ct)).Totals;
        foreach (var year in fx.Layout.ColdYears(null, null))
        {
            sum = sum.Add((await _search.SearchAsync(query, year, 0, ct)).Totals);
        }

        Assert.Equal(all.Totals.Count, sum.Count);
        AssertClose(all.Totals.Amount, sum.Amount);
        Assert.Equal(all.Totals.ByStatus, sum.ByStatus);
    }

    [Fact]
    public async Task Hot_tier_alone_returns_the_exact_first_page_when_it_has_enough_matches()
    {
        fx.RequireCluster();
        var ct = TestContext.Current.CancellationToken;

        var hot = await _search.SearchAsync(new PaymentQuery(), fx.Layout.Hot(null, null)!, 50, ct);
        var all = await _search.SearchAsync(new PaymentQuery(), fx.Layout.All(null, null)!, 50, ct);

        Assert.Equal(all.Hits.Select(p => p.Id), hot.Hits.Select(p => p.Id));
    }

    /// <summary>Sums come back from Elasticsearch as doubles; a few cents of drift over thousands of payments is fine.</summary>
    private static void AssertClose(decimal expected, decimal actual) => Assert.InRange(actual - expected, -0.05m, 0.05m);

    /// <summary>Same first page in the same order, and the same totals, as filtering the payments in memory.</summary>
    private static void AssertMatches(IEnumerable<Payment> expected, SearchPage page)
    {
        var matches = expected.ToList();
        var firstPage = matches
            .OrderByDescending(p => p.DepositDate)
            .ThenByDescending(p => p.Id, StringComparer.Ordinal)
            .Take(page.Hits.Count == 0 ? 0 : 50)
            .Select(p => p.Id);

        Assert.Equal(firstPage, page.Hits.Select(p => p.Id));
        Assert.Equal(matches.Count, page.Totals.Count);
        AssertClose(matches.Sum(p => p.Amount), page.Totals.Amount);
        Assert.Equal(
            matches.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => (long)g.Count()),
            page.Totals.ByStatus);
    }
}

[CollectionDefinition(nameof(ElasticsearchTests))]
public class ElasticsearchTests : ICollectionFixture<SearchFixture>;
