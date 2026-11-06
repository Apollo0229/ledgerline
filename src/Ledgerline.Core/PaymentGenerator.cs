using System.Globalization;

namespace Ledgerline.Core;

/// <summary>
/// Makes realistic-looking lockbox payments, the same ones every time. Each month is generated from
/// its own seed, so months can be generated in parallel, in any order, and re-seeding overwrites
/// documents in place instead of duplicating them.
/// </summary>
public sealed class PaymentGenerator(IndexLayout layout, int seed = 2026)
{
    private const int PayerCount = 6_000;

    /// <summary>
    /// How many payments each month gets out of <paramref name="total"/>. Volume grows over time, the newest
    /// full month has about twice the oldest, and the current month only counts the days elapsed so far.
    /// </summary>
    public IReadOnlyList<(DateOnly Month, int Count)> Plan(int total)
    {
        var months = layout.Months().ToList();
        var weights = months.Select((m, i) =>
        {
            var growth = 1.0 + (double)i / Math.Max(1, months.Count - 1);
            var elapsed = m.Year == layout.Today.Year && m.Month == layout.Today.Month
                ? (double)layout.Today.Day / DateTime.DaysInMonth(m.Year, m.Month)
                : 1.0;
            return growth * elapsed;
        }).ToList();

        var sum = weights.Sum();
        var counts = weights.Select(w => (int)Math.Floor(total * w / sum)).ToList();
        counts[^1] += total - counts.Sum(); // rounding remainder goes to the newest month
        return months.Zip(counts).ToList();
    }

    /// <summary>The payments deposited in <paramref name="month"/>, on business days up to today.</summary>
    public IEnumerable<Payment> Month(DateOnly month, int count)
    {
        var random = new Random(seed * 100_003 + month.Year * 12 + month.Month);
        var days = BusinessDays(month).ToArray();
        if (days.Length == 0) yield break;

        for (var i = 0; i < count; i++)
        {
            var client = Skewed(random, Names.Clients.Length);
            var status = Status(random);
            var invoices = Invoices(random);
            yield return new Payment
            {
                Id = $"{month:yyyyMM}-{i:D7}",
                DepositDate = days[random.Next(days.Length)],
                Client = Names.Clients[client],
                Lockbox = $"LB-{1000 + client * 7 + random.Next(1 + client % 3):D4}",
                Payer = Payer(Skewed(random, PayerCount)),
                CheckNumber = random.Next(1_000, 999_999).ToString(CultureInfo.InvariantCulture),
                Amount = Amount(random),
                Invoices = invoices,
                Status = status,
                Memo = Memo(random, status, invoices),
            };
        }
    }

    private IEnumerable<DateOnly> BusinessDays(DateOnly month)
    {
        for (var d = month; d.Month == month.Month && d <= layout.Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) yield return d;
        }
    }

    /// <summary>A few names get most of the traffic, like real receivables. Index 0 is the busiest.</summary>
    private static int Skewed(Random random, int count) =>
        Math.Min(count - 1, (int)(count * Math.Pow(random.NextDouble(), 2.2)));

    /// <summary>A stable, unique company name for each payer number.</summary>
    internal static string Payer(int n)
    {
        var first = Names.PayerFirst[n % Names.PayerFirst.Length];
        var second = Names.PayerSecond[n / Names.PayerFirst.Length % Names.PayerSecond.Length];
        var suffix = Names.PayerSuffix[n / (Names.PayerFirst.Length * Names.PayerSecond.Length) % Names.PayerSuffix.Length];
        return $"{first} {second} {suffix}";
    }

    /// <summary>Log-normal around $1,800: mostly hundreds to thousands, occasionally six figures.</summary>
    private static decimal Amount(Random random)
    {
        var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var dollars = Math.Clamp(Math.Exp(Math.Log(1_800) + 1.25 * normal), 5, 950_000);
        return Math.Round((decimal)dollars, 2);
    }

    private static PaymentStatus Status(Random random) => random.NextDouble() switch
    {
        < 0.955 => PaymentStatus.Posted,
        < 0.990 => PaymentStatus.Exception,
        _ => PaymentStatus.Returned,
    };

    private static string[] Invoices(Random random)
    {
        var first = random.Next(100_000, 999_000);
        return Enumerable.Range(0, 1 + (int)Math.Floor(Math.Pow(random.NextDouble(), 3) * 4))
            .Select(k => $"INV-{first + k}")
            .ToArray();
    }

    private static string Memo(Random random, PaymentStatus status, string[] invoices) => status switch
    {
        PaymentStatus.Returned => random.Next(3) switch
        {
            0 => "Returned item: insufficient funds",
            1 => "Returned item: account closed",
            _ => "Returned item: stop payment",
        },
        PaymentStatus.Exception => random.Next(3) switch
        {
            0 => "Remittance advice missing, invoice not identified",
            1 => $"Amount does not match {invoices[0]}, short paid",
            _ => "Payer not on file, manual review",
        },
        _ => invoices.Length == 1 ? $"Payment for {invoices[0]}" : $"Payment for {string.Join(", ", invoices)}",
    };
}
