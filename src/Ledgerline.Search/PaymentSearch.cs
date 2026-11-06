using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Ledgerline.Core;

namespace Ledgerline.Search;

/// <summary>Runs one search over one segment of history and returns a page of hits plus totals.</summary>
public sealed class PaymentSearch(ElasticsearchClient client)
{
    private const string AmountAgg = "amount";
    private const string StatusAgg = "status";

    public async Task<SearchPage> SearchAsync(PaymentQuery query, Segment segment, int size, CancellationToken ct = default)
    {
        var request = new SearchRequest(Indices.Parse(string.Join(',', segment.Indices), CultureInfo.InvariantCulture))
        {
            IgnoreUnavailable = true, // months with no deposits have no index
            Query = Build(query, segment),
            Size = size,
            Sort = [Newest("deposit_date"), Newest("id")],
            TrackTotalHits = new TrackHits(true),
            Aggregations = new Dictionary<string, Aggregation>
            {
                [AmountAgg] = new SumAggregation { Field = new Field("amount") },
                [StatusAgg] = new TermsAggregation { Field = new Field("status"), Size = 10 },
            },
        };

        var response = await client.SearchAsync<Payment>(request, ct).ConfigureAwait(false);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search over {segment.Label} failed: {response.DebugInformation}");
        }

        var byStatus = response.Aggregations!.GetStringTerms(StatusAgg)!.Buckets
            .ToDictionary(b => Enum.Parse<PaymentStatus>(b.Key.ToString()!, ignoreCase: true), b => b.DocCount);
        var amount = (decimal)(response.Aggregations!.GetSum(AmountAgg)!.Value ?? 0);

        return new SearchPage
        {
            Hits = response.Documents.ToList(),
            Totals = new Totals(response.Total, Math.Round(amount, 2), byStatus),
            TookMs = response.Took,
        };
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
