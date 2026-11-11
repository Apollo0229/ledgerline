using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ledgerline.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerline.Tests;

/// <summary>The HTTP surface, hosted in memory over the fixture's test indices.</summary>
[Collection(nameof(ElasticsearchTests))]
public sealed class ApiTests(SearchFixture fx) : IDisposable
{
    private readonly WebApplicationFactory<Program> _app = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(fx.Layout)));

    public void Dispose() => _app.Dispose();

    [Theory]
    [InlineData("min=10&max=5", "max")]
    [InlineData("from=2025-02-01&to=2025-01-01", "to")]
    [InlineData("size=500", "size")]
    [InlineData("status=bounced", "status")]
    [InlineData("cold=fastest", "cold")]
    public async Task Invalid_filters_are_rejected_with_a_named_field(string query, string field)
    {
        using var http = _app.CreateClient();
        var response = await http.GetAsync(new Uri($"/v1/search?{query}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("Exception")]
    public async Task Status_accepts_the_lowercase_names_the_api_returns(string status)
    {
        fx.RequireCluster();
        using var http = _app.CreateClient();

        using var body = JsonDocument.Parse(await http.GetStringAsync(
            new Uri($"/v1/search?status={status}&size=5", UriKind.Relative), TestContext.Current.CancellationToken));

        var expected = fx.Payments.Count(p => p.Status == PaymentStatus.Exception);
        var totals = body.RootElement.GetProperty("totals");
        Assert.Equal(expected, totals.GetProperty("count").GetInt64());
        Assert.Equal(expected, totals.GetProperty("by_status").GetProperty("exception").GetInt64());
        Assert.All(body.RootElement.GetProperty("hits").EnumerateArray(),
            h => Assert.Equal("exception", h.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Clients_lists_every_client_once_in_order()
    {
        fx.RequireCluster();
        using var http = _app.CreateClient();

        var clients = await http.GetFromJsonAsync<List<string>>(new Uri("/v1/clients", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(fx.Payments.Select(p => p.Client).Distinct().Order(StringComparer.Ordinal), clients);
    }

    [Fact]
    public async Task Search_can_break_totals_down_by_year_matching_the_overall_totals()
    {
        fx.RequireCluster();
        using var http = _app.CreateClient();

        using var plain = JsonDocument.Parse(await http.GetStringAsync(
            new Uri("/v1/search?status=exception&size=5", UriKind.Relative), TestContext.Current.CancellationToken));
        using var years = JsonDocument.Parse(await http.GetStringAsync(
            new Uri("/v1/search?status=exception&size=5&years=true", UriKind.Relative), TestContext.Current.CancellationToken));

        var byYear = years.RootElement.GetProperty("by_year").EnumerateObject().ToList();
        Assert.Equal(byYear.Select(y => y.Name).OrderDescending(StringComparer.Ordinal), byYear.Select(y => y.Name));
        var count = plain.RootElement.GetProperty("totals").GetProperty("count").GetInt64();
        Assert.Equal(count, years.RootElement.GetProperty("totals").GetProperty("count").GetInt64());
        Assert.Equal(count, byYear.Sum(y => y.Value.GetProperty("count").GetInt64()));
        Assert.Equal(
            Ids(plain.RootElement.GetProperty("hits")),
            Ids(years.RootElement.GetProperty("hits")));
    }

    private static List<string?> Ids(JsonElement hits) => hits.EnumerateArray().Select(h => h.GetProperty("id").GetString()).ToList();

    [Fact]
    public async Task Search_returns_a_page_newest_first_with_snake_case_fields()
    {
        fx.RequireCluster();
        using var http = _app.CreateClient();

        using var body = JsonDocument.Parse(await http.GetStringAsync(
            new Uri("/v1/search?size=3", UriKind.Relative), TestContext.Current.CancellationToken));

        var hits = body.RootElement.GetProperty("hits").EnumerateArray().ToList();
        Assert.Equal(3, hits.Count);
        var dates = hits.Select(h => h.GetProperty("deposit_date").GetString()).ToList();
        Assert.Equal(dates.OrderDescending(StringComparer.Ordinal), dates);
        Assert.True(body.RootElement.TryGetProperty("took_ms", out _));
        Assert.Equal(SearchFixture.Count, body.RootElement.GetProperty("totals").GetProperty("count").GetInt64());
    }
}
