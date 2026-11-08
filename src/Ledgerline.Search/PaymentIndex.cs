using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using System.Text.Json;
using Elastic.Transport;
using Ledgerline.Core;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Ledgerline.Search;

/// <summary>Creates, fills and seals the monthly payment indices.</summary>
public sealed class PaymentIndex(ElasticsearchClient client)
{
    /// <summary>Installs the index template every <c>payments-*</c> index is created from. Safe to repeat.</summary>
    public async Task EnsureTemplateAsync(CancellationToken ct = default)
    {
        await using var stream = typeof(PaymentIndex).Assembly.GetManifestResourceStream("payments-template.json")
            ?? throw new InvalidOperationException("payments-template.json is not embedded");
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        await SendAsync(HttpMethod.PUT, "/_index_template/payments", body, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a month finished loading. A load switches refreshes off and <see cref="SealAsync"/> switches
    /// them back on as its last step, so an index that still has them off was interrupted part way.
    /// </summary>
    public async Task<bool> IsCompleteAsync(string index, CancellationToken ct = default)
    {
        var response = await client.Transport.RequestAsync<StringResponse>(
            HttpMethod.GET, $"/{index}/_settings/index.refresh_interval", cancellationToken: ct).ConfigureAwait(false);
        if (response.ApiCallDetails.HttpStatusCode == 404) return false;
        if (!response.ApiCallDetails.HasSuccessfulStatusCode)
        {
            throw new InvalidOperationException($"Reading settings of {index} failed: {response.Body}");
        }

        using var json = JsonDocument.Parse(response.Body);
        var refresh = json.RootElement.GetProperty(index).GetProperty("settings").GetProperty("index").GetProperty("refresh_interval");
        return refresh.GetString() != "-1";
    }

    public async Task DeleteAsync(string index, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.DELETE, $"/{index}?ignore_unavailable=true", null, ct).ConfigureAwait(false);

    /// <summary>Creates an empty index tuned for a bulk load: no refreshes until it is sealed.</summary>
    public async Task CreateForLoadAsync(string index, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.PUT, $"/{index}", """{"settings":{"refresh_interval":"-1"}}""", ct).ConfigureAwait(false);

    /// <summary>Bulk-indexes payments by id, so loading the same payments again overwrites them.</summary>
    public async Task IndexAsync(string index, IReadOnlyCollection<Payment> payments, CancellationToken ct = default)
    {
        var response = await client.BulkAsync(new BulkRequest(index)
        {
            Operations = [.. payments.Select(p => new BulkIndexOperation<Payment>(p) { Id = p.Id })],
        }, ct).ConfigureAwait(false);

        if (!response.IsValidResponse || response.Errors)
        {
            var first = response.ItemsWithErrors.FirstOrDefault()?.Error?.Reason ?? response.DebugInformation;
            throw new InvalidOperationException($"Bulk load into {index} failed: {first}");
        }
    }

    /// <summary>
    /// Finishes a load. Hot months go back to refreshing every second. Cold months are made read-only and
    /// merged to a single segment, which is what keeps years of rarely-read history cheap to store and search.
    /// </summary>
    public async Task SealAsync(string index, bool hot, CancellationToken ct = default)
    {
        await client.Indices.RefreshAsync(index, ct).ConfigureAwait(false);
        if (!hot)
        {
            await SendAsync(HttpMethod.POST, $"/{index}/_forcemerge?max_num_segments=1", null, ct).ConfigureAwait(false);
        }

        // Last, because it marks the month complete (see IsCompleteAsync). Cold months also become read-only.
        var settings = hot
            ? """{"index":{"refresh_interval":"1s"}}"""
            : """{"index":{"refresh_interval":"1s","blocks":{"write":true}}}""";
        await SendAsync(HttpMethod.PUT, $"/{index}/_settings", settings, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var response = await client.Transport.RequestAsync<StringResponse>(
            method, path, body is null ? null : PostData.String(body), cancellationToken: ct).ConfigureAwait(false);
        if (!response.ApiCallDetails.HasSuccessfulStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} failed: {response.Body}");
        }
    }
}
