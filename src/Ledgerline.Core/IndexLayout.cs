using System.Globalization;

namespace Ledgerline.Core;

/// <summary>A slice of history searched in one request: a date range and the monthly indices that hold it.</summary>
public sealed record Segment(string Label, DateOnly From, DateOnly To, IReadOnlyList<string> Indices);

/// <summary>
/// Where payments live. One index per deposit month (<c>payments-2026.11</c>). The newest
/// <see cref="HotMonths"/> months are the hot tier, still written to and searched most; everything
/// older is cold: read-only and merged down to one segment.
/// </summary>
/// <remarks>Tests use their own <paramref name="prefix"/>, such as <c>payments-test-</c>, so they never touch seeded data.</remarks>
public sealed class IndexLayout(DateOnly today, int hotMonths = 3, int historyYears = 7, string prefix = "payments-")
{
    public string Prefix { get; } = prefix;

    public DateOnly Today { get; } = today;
    public int HotMonths { get; } = hotMonths;

    /// <summary>The first day of the oldest month kept.</summary>
    public DateOnly Earliest { get; } = FirstOfMonth(today).AddYears(-historyYears);

    /// <summary>The first day of the oldest hot month.</summary>
    public DateOnly HotStart { get; } = FirstOfMonth(today).AddMonths(-(hotMonths - 1));

    public string IndexFor(DateOnly date) => $"{Prefix}{date:yyyy.MM}";

    public bool IsHot(DateOnly date) => date >= HotStart;

    /// <summary>Every month from <see cref="Earliest"/> to today, oldest first.</summary>
    public IEnumerable<DateOnly> Months()
    {
        for (var m = Earliest; m <= Today; m = m.AddMonths(1)) yield return m;
    }

    /// <summary>The hot part of <paramref name="from"/>..<paramref name="to"/>, or null if the range is all cold.</summary>
    public Segment? Hot(DateOnly? from, DateOnly? to)
    {
        var (start, end) = Clamp(from, to);
        if (start < HotStart) start = HotStart;
        return start > end ? null : Make("hot", start, end);
    }

    /// <summary>The cold part of the range split into calendar years, newest first, so totals arrive in the order results are sorted.</summary>
    public IReadOnlyList<Segment> ColdYears(DateOnly? from, DateOnly? to)
    {
        var (start, end) = Clamp(from, to);
        var coldEnd = HotStart.AddDays(-1);
        if (end > coldEnd) end = coldEnd;

        var years = new List<Segment>();
        for (var year = end.Year; year >= start.Year; year--)
        {
            var yFrom = Max(start, new DateOnly(year, 1, 1));
            var yTo = Min(end, new DateOnly(year, 12, 31));
            if (yFrom <= yTo) years.Add(Make(year.ToString(CultureInfo.InvariantCulture), yFrom, yTo));
        }
        return years;
    }

    /// <summary>The whole range as one segment: what a single plain query would search.</summary>
    public Segment? All(DateOnly? from, DateOnly? to)
    {
        var (start, end) = Clamp(from, to);
        return start > end ? null : Make("all", start, end);
    }

    private (DateOnly, DateOnly) Clamp(DateOnly? from, DateOnly? to) =>
        (Max(from ?? Earliest, Earliest), Min(to ?? Today, Today));

    private Segment Make(string label, DateOnly from, DateOnly to)
    {
        var indices = new List<string>();
        for (var m = FirstOfMonth(from); m <= to; m = m.AddMonths(1)) indices.Add(IndexFor(m));
        return new Segment(label, from, to, indices);
    }

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
