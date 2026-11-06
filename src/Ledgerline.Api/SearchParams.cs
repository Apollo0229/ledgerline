using Ledgerline.Core;

namespace Ledgerline.Api;

/// <summary>Query-string filters, for example <c>/v1/search?text=acme&amp;min=5000&amp;from=2020-01-01</c>.</summary>
internal sealed record SearchParams(
    string? Text,
    string? Client,
    string? Status,
    decimal? Min,
    decimal? Max,
    DateOnly? From,
    DateOnly? To,
    int? Size)
{
    public const int DefaultSize = 50;
    public const int MaxSize = 200;

    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Status is not null && ParseStatus(Status) is null)
        {
            errors["status"] = [$"status must be one of: {string.Join(", ", Enum.GetNames<PaymentStatus>().Select(n => n.ToLowerInvariant()))}."];
        }
        if (Size is < 0 or > MaxSize) errors["size"] = [$"size must be between 0 and {MaxSize}."];
        if (Min is < 0) errors["min"] = ["min cannot be negative."];
        if (Min > Max) errors["max"] = ["max must be at least min."];
        if (From > To) errors["to"] = ["to must be on or after from."];
        if (Text is { Length: > 200 }) errors["text"] = ["text is limited to 200 characters."];
        return errors.Count == 0 ? null : errors;
    }

    /// <summary>Accepts the lowercase names the API returns (<c>exception</c>), unlike default enum binding.</summary>
    private static PaymentStatus? ParseStatus(string value) =>
        Enum.TryParse<PaymentStatus>(value, ignoreCase: true, out var s) && Enum.IsDefined(s) ? s : null;

    public PaymentQuery ToQuery() => new()
    {
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
        Client = string.IsNullOrWhiteSpace(Client) ? null : Client,
        Status = Status is null ? null : ParseStatus(Status),
        MinAmount = Min,
        MaxAmount = Max,
        From = From,
        To = To,
    };
}
