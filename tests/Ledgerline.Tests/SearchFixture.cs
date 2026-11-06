using Elastic.Clients.Elasticsearch;
using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

/// <summary>
/// Seeds a small history into a real Elasticsearch under its own prefix, and keeps the same payments
/// in memory as the ground truth every search is checked against. Without a reachable cluster
/// (ELASTICSEARCH_URL, default localhost:9200) the integration tests are skipped, not failed.
/// </summary>
public sealed class SearchFixture : IAsyncLifetime
{
    public const int Count = 20_000;

    public IndexLayout Layout { get; } = new(new DateOnly(2026, 11, 6), prefix: "payments-test-");
    public List<Payment> Payments { get; } = [];
    public ElasticsearchClient Client { get; } = ElasticConnection.Connect(
        new Uri(Environment.GetEnvironmentVariable("ELASTICSEARCH_URL") ?? "http://localhost:9200"));
    public bool Available { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Available = (await Client.PingAsync()).IsValidResponse;
        if (!Available) return;

        var index = new PaymentIndex(Client);
        var generator = new PaymentGenerator(Layout);
        await index.EnsureTemplateAsync();
        foreach (var (month, count) in generator.Plan(Count))
        {
            var name = Layout.IndexFor(month);
            var payments = generator.Month(month, count).ToList();
            Payments.AddRange(payments);

            await index.DeleteAsync(name);
            await index.CreateForLoadAsync(name);
            if (payments.Count > 0) await index.IndexAsync(name, payments);
            await index.SealAsync(name, Layout.IsHot(month));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!Available) return;
        var index = new PaymentIndex(Client);
        foreach (var month in Layout.Months()) await index.DeleteAsync(Layout.IndexFor(month));
    }

    /// <summary>Skips without a cluster locally. CI sets REQUIRE_ELASTICSEARCH=1 so a missing cluster fails instead of passing quietly.</summary>
    public void RequireCluster()
    {
        if (!Available && Environment.GetEnvironmentVariable("REQUIRE_ELASTICSEARCH") == "1")
        {
            Assert.Fail("REQUIRE_ELASTICSEARCH is set but Elasticsearch is not reachable.");
        }
        Assert.SkipUnless(Available, "Elasticsearch is not reachable; run `docker compose up -d elasticsearch`.");
    }
}
