using Ledgerline.Api;
using Ledgerline.Core;
using Ledgerline.Search;

var builder = WebApplication.CreateBuilder(args);

var url = new Uri(builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200");
builder.Services.AddSingleton(_ => ElasticConnection.Connect(url));
builder.Services.AddSingleton<PaymentSearch>();
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
    var segment = layout.All(query.From, query.To);
    if (segment is null) return Results.Ok(new SearchPage { Hits = [], Totals = Totals.Empty, TookMs = 0 });

    return Results.Ok(await search.SearchAsync(query, segment, p.Size ?? SearchParams.DefaultSize, ct));
});

app.Run();

/// <summary>Exposed so integration tests can host the API in memory.</summary>
public partial class Program;
