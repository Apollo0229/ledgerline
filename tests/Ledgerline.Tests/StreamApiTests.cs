using System.Net;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Ledgerline.Core;
using Ledgerline.Search;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerline.Tests;

/// <summary>GET /v1/search/stream, read the way a browser's EventSource would.</summary>
[Collection(nameof(ElasticsearchTests))]
public sealed class StreamApiTests(SearchFixture fx) : IDisposable
{
    private readonly WebApplicationFactory<Program> _app = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(fx.Layout)));

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Streams_page_then_progress_per_segment_then_done_matching_the_baseline()
    {
        fx.RequireCluster();
        using var http = _app.CreateClient();

        var events = await Read(http, "/v1/search/stream?status=returned&min=2000");

        Assert.Equal("page", events[0].EventType);
        Assert.Equal("done", events[^1].EventType);
        var segments = events.Where(e => e.EventType == "progress").Select(e => e.Data.GetProperty("segment").GetString());
        Assert.Equal(["hot", "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019"], segments);

        using var baseline = JsonDocument.Parse(await http.GetStringAsync(
            new Uri("/v1/search?status=returned&min=2000", UriKind.Relative), TestContext.Current.CancellationToken));
        var page = events.Last(e => e.EventType == "page").Data;
        Assert.True(page.GetProperty("complete").GetBoolean());
        Assert.Equal(Ids(baseline.RootElement.GetProperty("hits")), Ids(page.GetProperty("hits")));
        Assert.Equal(
            baseline.RootElement.GetProperty("totals").GetProperty("count").GetInt64(),
            events[^1].Data.GetProperty("totals").GetProperty("count").GetInt64());
        Assert.True(events[^1].Data.GetProperty("first_page_ms").GetInt64() <= events[^1].Data.GetProperty("at_ms").GetInt64());
    }

    [Fact]
    public async Task Invalid_filters_are_rejected_before_streaming_starts()
    {
        using var http = _app.CreateClient();

        var response = await http.GetAsync(new Uri("/v1/search/stream?min=10&max=5", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_failure_part_way_through_ends_the_stream_with_an_error_event()
    {
        using var app = _app.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ISegmentSearch>(new FailsOnColdYears())));
        using var http = app.CreateClient();

        var events = await Read(http, "/v1/search/stream");

        Assert.Equal(["progress", "error"], events.Select(e => e.EventType)); // no page yet: the hot tier had no matches
        Assert.Contains("failed", events[^1].Data.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static async Task<List<SseItem<JsonElement>>> Read(HttpClient http, string url)
    {
        var ct = TestContext.Current.CancellationToken;
        using var response = await http.GetAsync(new Uri(url, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var events = new List<SseItem<JsonElement>>();
        var parser = SseParser.Create(stream, (type, data) => JsonDocument.Parse(data.ToArray()).RootElement.Clone());
        await foreach (var item in parser.EnumerateAsync(ct)) events.Add(item);
        return events;
    }

    private static List<string?> Ids(JsonElement hits) => hits.EnumerateArray().Select(h => h.GetProperty("id").GetString()).ToList();

    /// <summary>The hot tier answers; the first cold year throws, as a lost node would.</summary>
    private sealed class FailsOnColdYears : ISegmentSearch
    {
        public Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default) =>
            segment.Label == "hot"
                ? Task.FromResult(new SearchPage { Hits = [], Totals = Totals.Empty, TookMs = 1 })
                : Task.FromException<SearchPage>(new InvalidOperationException("node lost"));
    }
}
