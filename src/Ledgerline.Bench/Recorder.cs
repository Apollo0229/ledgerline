using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Text.Json;

/// <summary>
/// Records real streamed searches for the portal's public demo, which replays them event by event at the
/// times they arrived. Every search runs with caches dropped first, a few times; the run with the median
/// first-page time is kept, next to the fair baseline's time for the same search under the same conditions.
/// </summary>
internal static class Recorder
{
    /// <summary>A demo search as the portal's filter bar would hold it, and the API query it becomes.</summary>
    private sealed record DemoSearch(string Id, string Label, Dictionary<string, string> Filters, string? Note = null)
    {
        public string Query => string.Join('&', Filters
            .Where(f => f.Key is not "range")
            .Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"));
    }

    public sealed record RecordedEvent(double T, string Type, JsonElement Data);

    public sealed record RecordedSearch(
        string Id, string Label, string? Note, Dictionary<string, string> Filters, string Query,
        double BaselineMs, double FirstPageMs, double DoneMs, long Matches, List<RecordedEvent> Events);

    public sealed record Recording(DateTime RecordedAt, string Note, BenchEnvironment Environment, List<string> Clients, List<RecordedSearch> Searches);

    public static async Task<Recording> RunAsync(HttpClient http, Uri elastic, BenchEnvironment environment, int runs)
    {
        using var old = JsonDocument.Parse(await http.GetStringAsync(new Uri("v1/search?from=2021-03-01&to=2021-03-31&size=1", UriKind.Relative)));
        var invoice = old.RootElement.GetProperty("hits")[0].GetProperty("invoices")[0].GetString()!;
        var clients = JsonSerializer.Deserialize<List<string>>(await http.GetStringAsync(new Uri("v1/clients", UriKind.Relative)))!;

        DemoSearch[] searches =
        [
            new("everything", "Everything, newest first", new() { ["range"] = "all" }),
            new("client-over-5k", "Harborview Health System over $5,000",
                new() { ["range"] = "all", ["client"] = "Harborview Health System", ["min"] = "5000" }),
            new("supply", "Free text: “Supply”", new() { ["range"] = "all", ["text"] = "Supply" }),
            new("exceptions", "Exceptions over $5,000", new() { ["range"] = "all", ["status"] = "exception", ["min"] = "5000" }),
            new("returned", "Returned items over $20,000", new() { ["range"] = "all", ["status"] = "returned", ["min"] = "20000" }),
            new("large", "Checks over $250,000", new() { ["range"] = "all", ["min"] = "250000" },
                "Rare: the newest three months hold only a few, so the page is topped up from cold history."),
            new("only-2021", "Only 2021", new() { ["range"] = "custom", ["from"] = "2021-01-01", ["to"] = "2021-12-31" },
                "No hot tier in range: the page comes from a page-only query over 2021."),
            new("needle", $"Needle: invoice {invoice}", new() { ["range"] = "all", ["text"] = invoice },
                "The weak spot: nearly nothing matches recently, so two-phase cannot win here."),
        ];

        var recorded = new List<RecordedSearch>();
        foreach (var s in searches)
        {
            // Warm up JIT and connections for this query shape, then measure from cold.
            await Fair(http, s.Query);
            await Stream(http, s.Query);

            var attempts = new List<(List<RecordedEvent> Events, double FirstPage)>();
            var baselines = new List<double>();
            for (var i = 0; i < runs; i++)
            {
                await Caches.DropAsync(http, elastic);
                baselines.Add(await Fair(http, s.Query));
                await Caches.DropAsync(http, elastic);
                var events = await Stream(http, s.Query);
                attempts.Add((events, events.First(e => e.Type == "page" && e.Data.GetProperty("complete").GetBoolean()).T));
            }

            var kept = attempts.OrderBy(a => a.FirstPage).ElementAt(attempts.Count / 2).Events;
            var baseline = baselines.Order().ElementAt(baselines.Count / 2);
            var done = kept.Single(e => e.Type == "done");
            var matches = done.Data.GetProperty("totals").GetProperty("count").GetInt64();
            var firstPage = kept.First(e => e.Type == "page" && e.Data.GetProperty("complete").GetBoolean()).T;
            recorded.Add(new RecordedSearch(s.Id, s.Label, s.Note, s.Filters, s.Query, Math.Round(baseline, 1),
                firstPage, done.T, matches, kept));
            Console.WriteLine($"{s.Label,-48} page {firstPage,6:F1} ms, done {done.T,6:F1} ms, fair baseline {baseline,6:F1} ms ({matches:N0} matches)");
        }

        return new Recording(DateTime.UtcNow,
            "Caches were dropped before every search, as for a query nobody has run recently.",
            environment, clients, recorded);
    }

    /// <summary>The fair baseline: one request delivering the same page, totals and per-year breakdown.</summary>
    private static async Task<double> Fair(HttpClient http, string query)
    {
        var clock = Stopwatch.StartNew();
        using var response = await http.GetAsync(new Uri($"v1/search?{query}&years=true", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsByteArrayAsync();
        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>Every event of one streamed search with the time it arrived, as a browser would see it.</summary>
    private static async Task<List<RecordedEvent>> Stream(HttpClient http, string query)
    {
        var clock = Stopwatch.StartNew();
        using var response = await http.GetAsync(new Uri($"v1/search/stream?{query}", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync();

        var events = new List<RecordedEvent>();
        await foreach (var e in SseParser.Create(body).EnumerateAsync())
        {
            var t = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
            using var data = JsonDocument.Parse(e.Data);
            events.Add(new RecordedEvent(t, e.EventType, data.RootElement.Clone()));
            if (e.EventType is "done" or "error") break;
        }
        return events;
    }
}
