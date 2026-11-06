using Elastic.Clients.Elasticsearch;

namespace Ledgerline.Search;

public static class ElasticConnection
{
    /// <summary>A client for the local cluster. Generous timeout: force-merging a cold month takes a while.</summary>
    public static ElasticsearchClient Connect(Uri url) =>
        new(new ElasticsearchClientSettings(url).RequestTimeout(TimeSpan.FromMinutes(5)));
}
