namespace Ledgerline.Core;

/// <summary>What a treasury analyst asks for. Every filter is optional; the date range defaults to all history kept.</summary>
public sealed record PaymentQuery
{
    /// <summary>Free text over payer, memo, invoice and check numbers.</summary>
    public string? Text { get; init; }

    public string? Client { get; init; }
    public PaymentStatus? Status { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>One page of matches, newest first, with totals over everything the searched segment matched.</summary>
public sealed record SearchPage
{
    public required IReadOnlyList<Payment> Hits { get; init; }
    public required Totals Totals { get; init; }

    /// <summary>Time Elasticsearch spent on the request, in milliseconds.</summary>
    public required long TookMs { get; init; }
}

/// <summary>Running totals. Two-phase search adds them up segment by segment.</summary>
public sealed record Totals(long Count, decimal Amount, IReadOnlyDictionary<PaymentStatus, long> ByStatus)
{
    public static readonly Totals Empty = new(0, 0, new Dictionary<PaymentStatus, long>());

    public Totals Add(Totals other) => new(
        Count + other.Count,
        Amount + other.Amount,
        Enum.GetValues<PaymentStatus>()
            .Select(s => (s, n: ByStatus.GetValueOrDefault(s) + other.ByStatus.GetValueOrDefault(s)))
            .Where(x => x.n > 0)
            .ToDictionary(x => x.s, x => x.n));
}

/// <summary>A page and totals over several years at once, with the totals also broken down by calendar year.</summary>
public sealed record YearSearch(SearchPage Page, IReadOnlyDictionary<int, Totals> ByYear);
