using System.Globalization;
using Ledgerline.Api;
using Ledgerline.Core;
using Ledgerline.Search;

var builder = WebApplication.CreateBuilder(args);

var url = new Uri(builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200");
builder.Services.AddSingleton(_ => ElasticConnection.Connect(url));
builder.Services.AddSingleton<PaymentSearch>();
builder.Services.AddSingleton<ISegmentSearch>(sp => sp.GetRequiredService<PaymentSearch>());
builder.Services.AddSingleton<TwoPhaseSearch>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
    new IndexLayout(DateOnly.FromDateTime(sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime)));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var v1 = app.MapGroup("/v1");

// The baseline: one query across every month in the range. Kept so two-phase search can be measured against it.
v1.MapGet("/search", async ([AsParameters] SearchParams p, PaymentSearch search, IndexLayout layout, CancellationToken ct) =>
{
    if (p.Validate() is { } problem) return Results.ValidationProblem(problem);

    var query = p.ToQuery();
    var size = p.Size ?? SearchParams.DefaultSize;
    var segment = layout.All(query.From, query.To);
    if (segment is null) return Results.Ok(new SearchPage { Hits = [], Totals = Totals.Empty, TookMs = 0 });
    if (!p.ByYear) return Results.Ok(await search.SearchAsync(query, segment, size, ct));

    // The same information the stream delivers, in one response: the fair comparison for the stream.
    var years = await search.SearchByYearAsync(query, segment, size, ct);
    return Results.Ok(new
    {
        years.Page.Hits,
        years.Page.Totals,
        years.Page.TookMs,
        ByYear = years.ByYear.OrderByDescending(y => y.Key).ToDictionary(y => y.Key.ToString(CultureInfo.InvariantCulture), y => y.Value),
    });
});

v1.MapGet("/clients", async (PaymentSearch search, IndexLayout layout, CancellationToken ct) =>
    Results.Ok(await search.ClientsAsync(layout.All(null, null)!, ct)));

// Two-phase: page 1 from the hot tier at once, then totals for each cold year, over Server-Sent Events.
// Closing the connection cancels the request token, which stops the searches still to run.
v1.MapGet("/search/stream", IResult ([AsParameters] SearchParams p, TwoPhaseSearch search, ILogger<Program> logger, CancellationToken ct) =>
{
    if (p.Validate() is { } problem) return Results.ValidationProblem(problem);

    var events = search.RunAsync(p.ToQuery(), p.Size ?? SearchParams.DefaultSize, p.ColdStrategy ?? search.Cold, ct);
    return TypedResults.ServerSentEvents(SearchStream.ToSse(events, logger, ct));
});

app.Run();

/// <summary>Exposed so integration tests can host the API in memory.</summary>
public partial class Program;
