using System.Text.Json.Serialization;

namespace Ledgerline.Core;

/// <summary>One check or electronic payment received at a lockbox and deposited for a treasury client.</summary>
public sealed record Payment
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("deposit_date")] public required DateOnly DepositDate { get; init; }
    [JsonPropertyName("client")] public required string Client { get; init; }
    [JsonPropertyName("lockbox")] public required string Lockbox { get; init; }
    [JsonPropertyName("payer")] public required string Payer { get; init; }
    [JsonPropertyName("check_number")] public required string CheckNumber { get; init; }
    [JsonPropertyName("amount")] public required decimal Amount { get; init; }
    [JsonPropertyName("invoices")] public required IReadOnlyList<string> Invoices { get; init; }
    [JsonPropertyName("status")] public required PaymentStatus Status { get; init; }
    [JsonPropertyName("memo")] public required string Memo { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    /// <summary>Matched to open invoices and posted to the client's receivables.</summary>
    [JsonStringEnumMemberName("posted")] Posted,

    /// <summary>Deposited, but could not be matched automatically; needs a person.</summary>
    [JsonStringEnumMemberName("exception")] Exception,

    /// <summary>Returned by the payer's bank, for example for insufficient funds.</summary>
    [JsonStringEnumMemberName("returned")] Returned,
}
