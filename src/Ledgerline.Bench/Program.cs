// Measures the single-query baseline against the two-phase stream (both cold strategies), over HTTP,
// as a browser sees them.
//
//   dotnet run -c Release --project src/Ledgerline.Bench -- warm --runs 30
//   dotnet run -c Release --project src/Ledgerline.Bench -- cold --runs 8
//   dotnet run -c Release --project src/Ledgerline.Bench -- load --users 16 --seconds 30
//
// cold clears Elasticsearch's caches and drops the operating system's page cache inside Docker's VM
// before every request (it runs a privileged alpine container), so each search reads from disk.

using System.Diagnostics;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Serialization;

var inv = CultureInfo.InvariantCulture;
string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

var mode = args.FirstOrDefault() ?? "warm";
var api = new Uri(Arg("--url") ?? "http://127.0.0.1:8080/");
var elastic = new Uri(Arg("--elasticsearch") ?? "http://127.0.0.1:9200/");
var runs = int.Parse(Arg("--runs") ?? "30", inv);
var users = int.Parse(Arg("--users") ?? "16", inv);
var seconds = int.Parse(Arg("--seconds") ?? "30", inv);
var output = Arg("--out");

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 512, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = api,
    Timeout = TimeSpan.FromMinutes(2),
};
var bench = new Bench(http);
var scenarios = await Scenarios.LoadAsync(http);

var environment = await Bench.DescribeAsync(http, elastic);
Console.WriteLine($"{environment.Payments:N0} payments in {environment.Indices} indices, Elasticsearch {environment.ElasticsearchVersion}, {Environment.ProcessorCount} logical CPUs");

string[] Streams = ["combined", "years"];

object result = mode switch
{
    "warm" => await RunSingle(cold: false),
    "cold" => await RunSingle(cold: true),
    "load" => await RunLoad(),
    _ => throw new ArgumentException($"unknown mode {mode}; use warm, cold or load"),
};

if (output is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { mode, environment, result }, Json.Options));
    Console.WriteLine($"wrote {output}");
}


async Task<List<SingleResult>> RunSingle(bool cold)
{
    // Warm up every endpoint so JIT and connection set-up are not measured.
    foreach (var s in scenarios)
    {
        await bench.BaselineAsync(s.Query);
        foreach (var c in Streams) await bench.StreamAsync(s.Query, c);
    }

    var results = new List<SingleResult>();
    foreach (var s in scenarios)
    {
        var baseline = new List<double>();
        var streams = Streams.ToDictionary(c => c, _ => new List<Timing>());
        for (var i = 0; i < runs; i++)
        {
            // Rotate which goes first, so none always benefits from another's leftovers.
            for (var k = 0; k < 3; k++)
            {
                if (cold) await Caches.DropAsync(http, elastic);
                var which = (i + k) % 3;
                if (which == 0) baseline.Add(await bench.BaselineAsync(s.Query));
                else streams[Streams[which - 1]].Add(await bench.StreamAsync(s.Query, Streams[which - 1]));
            }
        }

        var r = new SingleResult(s.Name, s.Query, Stats.Of(baseline),
            StreamStats.Of(streams["combined"]), StreamStats.Of(streams["years"]), streams["combined"][0].Matches);
        results.Add(r);
        Console.WriteLine($"{s.Name,-30} baseline {r.Baseline.P50,6:F1} | combined page {r.Combined.ExactPage.P50,6:F1} done {r.Combined.Done.P50,6:F1} | years page {r.Years.ExactPage.P50,6:F1} done {r.Years.Done.P50,6:F1} ms (p50, {r.Matches:N0} matches)");
    }
    return results;
}

async Task<List<LoadResult>> RunLoad()
{
    var results = new List<LoadResult>();
    foreach (var endpoint in new[] { "baseline", "combined", "years" })
    {
        await RunFor(endpoint, TimeSpan.FromSeconds(5), new List<double>(), new List<Timing>()); // warm-up, discarded
        var baseline = new List<double>();
        var stream = new List<Timing>();
        var clock = Stopwatch.StartNew();
        await RunFor(endpoint, TimeSpan.FromSeconds(seconds), baseline, stream);
        var elapsed = clock.Elapsed.TotalSeconds;

        var r = endpoint == "baseline"
            ? new LoadResult(endpoint, users, baseline.Count / elapsed, Stats.Of(baseline), Stats.Of(baseline))
            : new LoadResult(endpoint, users, stream.Count / elapsed, Stats.Of(stream.Select(t => t.ExactPageMs)), Stats.Of(stream.Select(t => t.DoneMs)));
        results.Add(r);
        Console.WriteLine($"{endpoint,-8} {users} users: {r.RequestsPerSecond,6:F0} searches/s | page p50 {r.Page.P50,7:F1} p95 {r.Page.P95,7:F1} | complete p50 {r.Complete.P50,7:F1} p95 {r.Complete.P95,7:F1} ms");
    }
    return results;
}

async Task RunFor(string endpoint, TimeSpan duration, List<double> baseline, List<Timing> stream)
{
    var until = DateTime.UtcNow + duration;
    await Parallel.ForEachAsync(Enumerable.Range(0, users), new ParallelOptions { MaxDegreeOfParallelism = users }, async (user, _) =>
    {
        // Every user walks the same mix of scenarios, starting at a different one.
        for (var n = user; DateTime.UtcNow < until; n++)
        {
            var query = scenarios[n % scenarios.Count].Query;
            if (endpoint == "baseline")
            {
                var ms = await bench.BaselineAsync(query);
                lock (baseline) baseline.Add(ms);
            }
            else
            {
                var t = await bench.StreamAsync(query, endpoint);
                lock (stream) stream.Add(t);
            }
        }
    });
}

internal sealed record Scenario(string Name, string Query);

internal static class Scenarios
{
    /// <summary>From broad to needle-in-a-haystack. The invoice is looked up from 2021 so it exists in any seeded copy.</summary>
    public static async Task<List<Scenario>> LoadAsync(HttpClient http)
    {
        using var old = JsonDocument.Parse(await http.GetStringAsync(new Uri("v1/search?from=2021-03-01&to=2021-03-31&size=1", UriKind.Relative)));
        var invoice = old.RootElement.GetProperty("hits")[0].GetProperty("invoices")[0].GetString();
        return
        [
            new("Everything, newest first", ""),
            new("One client over $5,000", "client=Harborview%20Health%20System&min=5000"),
            new("Free text \"Supply\"", "text=Supply"),
            new("Exceptions over $5,000", "status=exception&min=5000"),
            new("Returned items over $20,000", "status=returned&min=20000"),
            new("Needle: one 2021 invoice", $"text={invoice}"),
            new("Only 2021", "from=2021-01-01&to=2021-12-31"),
        ];
    }
}

internal sealed record Timing(double FirstPageMs, double ExactPageMs, double DoneMs, long Matches);

internal sealed class Bench(HttpClient http)
{
    /// <summary>One plain query: the page and the totals arrive together, so this is both time to first result and to all of it.</summary>
    public async Task<double> BaselineAsync(string query)
    {
        var clock = Stopwatch.StartNew();
        using var response = await http.GetAsync(new Uri($"v1/search?{query}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsByteArrayAsync();
        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The stream: time to the first page event (which may still grow), to the first complete page (the same
    /// page the baseline returns) and to done (the same totals).
    /// </summary>
    public async Task<Timing> StreamAsync(string query, string cold)
    {
        var clock = Stopwatch.StartNew();
        using var response = await http.GetAsync(new Uri($"v1/search/stream?{query}&cold={cold}", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync();

        double? first = null, exact = null;
        await foreach (var e in SseParser.Create(body).EnumerateAsync())
        {
            var now = clock.Elapsed.TotalMilliseconds;
            if (e.EventType == "page")
            {
                first ??= now;
                if (exact is null && e.Data.Contains("\"complete\":true", StringComparison.Ordinal)) exact = now;
            }
            else if (e.EventType == "done")
            {
                using var done = JsonDocument.Parse(e.Data);
                var matches = done.RootElement.GetProperty("totals").GetProperty("count").GetInt64();
                return new Timing(first ?? now, exact ?? now, now, matches);
            }
            else if (e.EventType == "error")
            {
                throw new InvalidOperationException($"stream failed: {e.Data}");
            }
        }
        throw new InvalidOperationException("stream ended without a done event");
    }

    public static async Task<BenchEnvironment> DescribeAsync(HttpClient http, Uri elastic)
    {
        using var es = new HttpClient { BaseAddress = elastic };
        using var root = JsonDocument.Parse(await es.GetStringAsync(new Uri("/", UriKind.Relative)));
        using var count = JsonDocument.Parse(await es.GetStringAsync(new Uri("/payments-*/_count", UriKind.Relative)));
        var indices = (await es.GetStringAsync(new Uri("/_cat/indices/payments-*?h=index", UriKind.Relative)))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return new BenchEnvironment(
            DateTime.UtcNow,
            count.RootElement.GetProperty("count").GetInt64(),
            indices,
            root.RootElement.GetProperty("version").GetProperty("number").GetString()!,
            Environment.ProcessorCount,
            http.BaseAddress!.ToString());
    }
}

internal static class Caches
{
    /// <summary>Empties Elasticsearch's request, query and field caches, then the page cache of Docker's VM.</summary>
    public static async Task DropAsync(HttpClient http, Uri elastic)
    {
        using var es = new HttpClient { BaseAddress = elastic };
        (await es.PostAsync(new Uri("/payments-*/_cache/clear", UriKind.Relative), null)).EnsureSuccessStatusCode();

        using var drop = Process.Start(new ProcessStartInfo("docker",
            ["run", "--rm", "--privileged", "--pid=host", "alpine:3.22", "sh", "-c", "sync; echo 3 > /proc/sys/vm/drop_caches"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        await drop.WaitForExitAsync();
        if (drop.ExitCode != 0) throw new InvalidOperationException($"dropping the page cache failed: {await drop.StandardError.ReadToEndAsync()}");
    }
}

internal sealed record BenchEnvironment(DateTime MeasuredAt, long Payments, int Indices, string ElasticsearchVersion, int LogicalCpus, string Api);

internal sealed record Stats(int N, double P50, double P95, double P99, double Max)
{
    /// <summary>Nearest-rank percentiles.</summary>
    public static Stats Of(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        if (v.Length == 0) return new Stats(0, 0, 0, 0, 0);
        double P(double p) => v[Math.Clamp((int)Math.Ceiling(p * v.Length) - 1, 0, v.Length - 1)];
        return new Stats(v.Length, P(0.50), P(0.95), P(0.99), v[^1]);
    }
}

internal sealed record StreamStats(Stats FirstPage, Stats ExactPage, Stats Done)
{
    public static StreamStats Of(List<Timing> t) =>
        new(Stats.Of(t.Select(x => x.FirstPageMs)), Stats.Of(t.Select(x => x.ExactPageMs)), Stats.Of(t.Select(x => x.DoneMs)));
}

internal sealed record SingleResult(string Scenario, string Query, Stats Baseline, StreamStats Combined, StreamStats Years, long Matches);

internal sealed record LoadResult(string Endpoint, int Users, double RequestsPerSecond, Stats Page, Stats Complete);

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
}
