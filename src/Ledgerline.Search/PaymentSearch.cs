using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Ledgerline.Core;

namespace Ledgerline.Search;

/// <summary>Runs one search over one segment of history and returns a page of hits plus totals.</summary>
public sealed class PaymentSearch(ElasticsearchClient client) : ISegmentSearch
{
    private const string AmountAgg = "amount";
    private const string StatusAgg = "status";
    private const string YearsAgg = "years";

    public async Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default) =>
        (await RunAsync(query, segment, size, byYear: false, ct).ConfigureAwait(false)).Page;

    /// <summary>One request over several years that also returns the totals of each calendar year.</summary>
    public Task<YearSearch> SearchByYearAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default) =>
        RunAsync(query, segment, size, byYear: true, ct);

    private async Task<YearSearch> RunAsync(PaymentQuery query, Segment segment, int size, bool byYear, CancellationToken ct)
    {
        var aggregations = TotalsAggregations();
        if (byYear)
        {
            aggregations[YearsAgg] = new Aggregation
            {
                DateHistogram = new DateHistogramAggregation
                {
                    Field = new Field("deposit_date"),
                    CalendarInterval = CalendarInterval.Year,
                    MinDocCount = 1,
                },
                Aggregations = TotalsAggregations(),
            };
        }

        var request = new SearchRequest(Indices.Parse(string.Join(',', segment.Indices), CultureInfo.InvariantCulture))
        {
            IgnoreUnavailable = true, // months with no deposits have no index
            Query = Build(query, segment),
            Size = size,
            Sort = [Newest("deposit_date"), Newest("id")],
            TrackTotalHits = new TrackHits(true),
            Aggregations = aggregations,
        };

        var response = await client.SearchAsync<Payment>(request, ct).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search over {segment.Label} failed: {response.DebugInformation}");
        }

        var page = new SearchPage
        {
            Hits = response.Documents.ToList(),
            Totals = ReadTotals(response.Total, response.Aggregations!),
            TookMs = response.Took,
        };
        var years = byYear
            ? response.Aggregations!.GetDateHistogram(YearsAgg)!.Buckets
                .ToDictionary(b => b.Key.UtcDateTime.Year, b => ReadTotals(b.DocCount, b.Aggregations!))
            : new Dictionary<int, Totals>();
        return new YearSearch(page, years);
    }

    /// <summary>Sum of amounts and count per status; used for the whole segment and inside each year.</summary>
    private static Dictionary<string, Aggregation> TotalsAggregations() => new()
    {
        [AmountAgg] = new SumAggregation { Field = new Field("amount") },
        [StatusAgg] = new TermsAggregation { Field = new Field("status"), Size = 10 },
    };

    private static Totals ReadTotals(long count, AggregateDictionary aggregations)
    {
        var byStatus = aggregations.GetStringTerms(StatusAgg)!.Buckets
            .ToDictionary(b => Enum.Parse<PaymentStatus>(b.Key.ToString()!, ignoreCase: true), b => b.DocCount);
        var amount = (decimal)(aggregations.GetSum(AmountAgg)!.Value ?? 0);
        return new Totals(count, Math.Round(amount, 2), byStatus);
    }

    /// <summary>Every treasury client with payments in <paramref name="segment"/>, by name, for the filter list.</summary>
    public async Task<IReadOnlyList<string>> ClientsAsync(Segment segment, CancellationToken ct = default)
    {
        var response = await client.SearchAsync<Payment>(new SearchRequest(Indices.Parse(string.Join(',', segment.Indices), CultureInfo.InvariantCulture))
        {
            IgnoreUnavailable = true,
            Size = 0,
            Aggregations = new Dictionary<string, Aggregation>
            {
                ["clients"] = new TermsAggregation { Field = new Field("client"), Size = 500 },
            },
        }, ct).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Listing clients failed: {response.DebugInformation}");
        }

        return response.Aggregations!.GetStringTerms("clients")!.Buckets
            .Select(b => b.Key.ToString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The Elasticsearch query for <paramref name="query"/> restricted to <paramref name="segment"/>'s dates.</summary>
    internal static Query Build(PaymentQuery query, Segment segment)
    {
        var filter = new List<Query>
        {
            new DateRangeQuery(new Field("deposit_date"))
            {
                Gte = Date(segment.From),
                Lte = Date(segment.To),
                Format = "strict_date",
            },
        };

        if (query.Client is { } c) filter.Add(new TermQuery(new Field("client"), c));
        if (query.Status is { } s) filter.Add(new TermQuery(new Field("status"), s.ToString().ToLowerInvariant()));
        if (query.MinAmount is not null || query.MaxAmount is not null)
        {
            filter.Add(new NumberRangeQuery(new Field("amount"))
            {
                Gte = (double?)query.MinAmount,
                Lte = (double?)query.MaxAmount,
            });
        }

        var must = new List<Query>();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            must.Add(new MultiMatchQuery(query.Text)
            {
                Fields = Fields.FromStrings(["payer^2", "memo", "invoices", "check_number", "lockbox"]),
                Operator = Operator.And,
                Lenient = true,
            });
        }

        return new BoolQuery { Filter = filter, Must = must };
    }

    private static SortOptions Newest(string field) =>
        new() { Field = new FieldSort(new Field(field)) { Order = SortOrder.Desc } };

    private static DateMathExpression Date(DateOnly d) => DateMath.Anchored(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
