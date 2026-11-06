using Ledgerline.Core;

namespace Ledgerline.Tests;

public class PaymentGeneratorTests
{
    private static readonly IndexLayout Layout = new(new DateOnly(2026, 11, 6));
    private static readonly PaymentGenerator Generator = new(Layout);

    [Fact]
    public void Plan_spreads_exactly_the_total_with_growth_over_time()
    {
        var plan = Generator.Plan(1_000_000);

        Assert.Equal(1_000_000, plan.Sum(p => p.Count));
        Assert.Equal(Layout.Months(), plan.Select(p => p.Month));
        var oldest = plan[0].Count;
        var newestFull = plan[^2].Count;
        Assert.InRange((double)newestFull / oldest, 1.9, 2.1);
    }

    [Fact]
    public void The_same_month_is_generated_identically_every_time()
    {
        var month = new DateOnly(2025, 2, 1);
        Assert.Equal(Generator.Month(month, 200), Generator.Month(month, 200), new PaymentComparer());
    }

    [Fact]
    public void Payments_land_on_business_days_inside_their_month_and_not_in_the_future()
    {
        var month = new DateOnly(2026, 11, 1);
        var payments = Generator.Month(month, 500).ToList();

        Assert.Equal(500, payments.Count);
        Assert.All(payments, p =>
        {
            Assert.Equal(11, p.DepositDate.Month);
            Assert.True(p.DepositDate <= Layout.Today);
            Assert.DoesNotContain(p.DepositDate.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        });
    }

    [Fact]
    public void Ids_are_unique_and_stable_so_reseeding_overwrites_in_place()
    {
        var payments = Generator.Month(new DateOnly(2023, 7, 1), 1_000).ToList();
        Assert.Equal(1_000, payments.Select(p => p.Id).Distinct().Count());
        Assert.Equal("202307-0000000", payments[0].Id);
    }

    [Fact]
    public void Statuses_amounts_and_memos_look_like_a_real_lockbox()
    {
        var payments = Generator.Month(new DateOnly(2024, 4, 1), 20_000).ToList();

        var posted = payments.Count(p => p.Status == PaymentStatus.Posted) / (double)payments.Count;
        Assert.InRange(posted, 0.94, 0.97);
        Assert.All(payments, p => Assert.InRange(p.Amount, 5m, 950_000m));
        Assert.All(payments, p => Assert.Equal(p.Amount, decimal.Round(p.Amount, 2)));

        var median = payments.Select(p => p.Amount).Order().ElementAt(payments.Count / 2);
        Assert.InRange(median, 1_500m, 2_100m);

        Assert.All(payments.Where(p => p.Status == PaymentStatus.Returned), p => Assert.StartsWith("Returned item", p.Memo));
        Assert.All(payments.Where(p => p.Status == PaymentStatus.Posted), p => Assert.Contains(p.Invoices[0], p.Memo));
    }

    [Fact]
    public void Payer_names_are_unique_per_number()
    {
        var names = Enumerable.Range(0, 6_000).Select(PaymentGenerator.Payer).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    private sealed class PaymentComparer : IEqualityComparer<Payment>
    {
        public bool Equals(Payment? a, Payment? b) =>
            a is not null && b is not null && a with { Invoices = [] } == b with { Invoices = [] } && a.Invoices.SequenceEqual(b.Invoices);

        public int GetHashCode(Payment p) => p.Id.GetHashCode();
    }
}
