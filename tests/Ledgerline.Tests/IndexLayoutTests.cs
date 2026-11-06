using Ledgerline.Core;

namespace Ledgerline.Tests;

public class IndexLayoutTests
{
    private static readonly IndexLayout Layout = new(new DateOnly(2026, 11, 6));

    [Fact]
    public void Names_one_index_per_month()
    {
        Assert.Equal("payments-2026.03", Layout.IndexFor(new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void Keeps_seven_years_and_three_hot_months()
    {
        Assert.Equal(new DateOnly(2019, 11, 1), Layout.Earliest);
        Assert.Equal(new DateOnly(2026, 9, 1), Layout.HotStart);
        Assert.Equal(85, Layout.Months().Count());
    }

    [Fact]
    public void Hot_segment_covers_the_newest_months_only()
    {
        var hot = Layout.Hot(null, null)!;
        Assert.Equal(new DateOnly(2026, 9, 1), hot.From);
        Assert.Equal(new DateOnly(2026, 11, 6), hot.To);
        Assert.Equal(["payments-2026.09", "payments-2026.10", "payments-2026.11"], hot.Indices);
    }

    [Fact]
    public void Hot_segment_is_null_when_the_range_is_all_cold()
    {
        Assert.Null(Layout.Hot(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)));
    }

    [Fact]
    public void Cold_history_is_split_into_years_newest_first()
    {
        var years = Layout.ColdYears(new DateOnly(2024, 6, 15), null);

        Assert.Equal(["2026", "2025", "2024"], years.Select(y => y.Label));
        Assert.Equal(new DateOnly(2026, 8, 31), years[0].To);
        Assert.Equal(8, years[0].Indices.Count); // January to August; September onward is hot
        Assert.Equal(new DateOnly(2024, 6, 15), years[^1].From);
        Assert.Equal("payments-2024.06", years[^1].Indices[0]);
    }

    [Fact]
    public void Ranges_are_clamped_to_the_history_kept()
    {
        var all = Layout.All(new DateOnly(2001, 1, 1), new DateOnly(2030, 1, 1))!;
        Assert.Equal(Layout.Earliest, all.From);
        Assert.Equal(Layout.Today, all.To);
        Assert.Equal(85, all.Indices.Count);
    }

    [Fact]
    public void Hot_and_cold_together_cover_the_whole_range_once()
    {
        var hot = Layout.Hot(null, null)!.Indices;
        var cold = Layout.ColdYears(null, null).SelectMany(y => y.Indices);
        Assert.Equal(Layout.All(null, null)!.Indices.Order(), hot.Concat(cold).Order());
    }
}
