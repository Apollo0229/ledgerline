// Loads years of synthetic lockbox payments into Elasticsearch, one index per month.
//
//   dotnet run --project src/Ledgerline.Seeder -- --count 5000000
//
// Re-running is safe: a month that finished loading is kept as it is, and a missing or interrupted month
// is rebuilt from scratch, so an interrupted load can simply be started again. Completed months are never
// reloaded just because the plan moved on a day, so searches keep working while new months are added.

using System.Diagnostics;
using System.Globalization;
using Ledgerline.Core;
using Ledgerline.Search;

var options = Options.Parse(args);
var layout = new IndexLayout(options.Today);
var generator = new PaymentGenerator(layout);
var client = ElasticConnection.Connect(options.Url);
var index = new PaymentIndex(client);

await index.EnsureTemplateAsync();
var plan = generator.Plan(options.Count);
Console.WriteLine($"Seeding {options.Count:N0} payments into {plan.Count} monthly indices, {layout.Earliest:yyyy-MM} to {layout.Today:yyyy-MM}");

var clock = Stopwatch.StartNew();
long loaded = 0, skipped = 0;
await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = options.Parallel }, async (entry, ct) =>
{
    var (month, count) = entry;
    var name = layout.IndexFor(month);
    if (await index.IsCompleteAsync(name, ct))
    {
        Interlocked.Increment(ref skipped);
        return;
    }

    await index.DeleteAsync(name, ct);
    await index.CreateForLoadAsync(name, ct);
    foreach (var batch in generator.Month(month, count).Chunk(5_000))
    {
        await index.IndexAsync(name, batch, ct);
    }
    var hot = layout.IsHot(month);
    await index.SealAsync(name, hot, ct);

    var total = Interlocked.Add(ref loaded, count);
    Console.WriteLine($"  {name}  {count,9:N0}  {(hot ? "hot " : "cold")}  ({total:N0} loaded, {clock.Elapsed:mm\\:ss})");
});

var rate = loaded / Math.Max(clock.Elapsed.TotalSeconds, 0.001);
Console.WriteLine($"Done in {clock.Elapsed:mm\\:ss}: {loaded:N0} loaded ({rate:N0}/s), {skipped} months already complete");

internal sealed record Options(Uri Url, int Count, DateOnly Today, int Parallel)
{
    public static Options Parse(string[] args)
    {
        string? Get(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var inv = CultureInfo.InvariantCulture;
        return new Options(
            new Uri(Get("--url") ?? Environment.GetEnvironmentVariable("ELASTICSEARCH_URL") ?? "http://localhost:9200"),
            int.Parse(Get("--count") ?? "2000000", inv),
            Get("--today") is { } t ? DateOnly.Parse(t, inv) : DateOnly.FromDateTime(DateTime.UtcNow),
            int.Parse(Get("--parallel") ?? "4", inv));
    }
}
