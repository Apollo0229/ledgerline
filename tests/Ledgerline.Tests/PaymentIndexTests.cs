using Ledgerline.Core;
using Ledgerline.Search;

namespace Ledgerline.Tests;

[Collection(nameof(ElasticsearchTests))]
public class PaymentIndexTests(SearchFixture fx)
{
    [Fact]
    public async Task A_month_counts_as_complete_only_once_it_has_been_sealed()
    {
        fx.RequireCluster();
        var ct = TestContext.Current.CancellationToken;
        var index = new PaymentIndex(fx.Client);
        const string name = "payments-test-sealing";

        await index.DeleteAsync(name, ct);
        Assert.False(await index.IsCompleteAsync(name, ct)); // missing

        await index.CreateForLoadAsync(name, ct);
        await index.IndexAsync(name, new PaymentGenerator(fx.Layout).Month(new DateOnly(2021, 3, 1), 100).ToList(), ct);
        Assert.False(await index.IsCompleteAsync(name, ct)); // loaded but interrupted before sealing

        await index.SealAsync(name, hot: false, ct);
        Assert.True(await index.IsCompleteAsync(name, ct));

        await index.DeleteAsync(name, ct);
    }
}
