using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class PayPalDateRangeSplitting
{
    [Fact]
    public void SplitsRangesLongerThan31Days()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

        var windows = PayPalPaymentsClient.SplitDateRange(from, to).ToList();

        Assert.True(windows.Count >= 3);
        Assert.Equal(from, windows[0].From);
        Assert.Equal(to, windows[^1].To);
        Assert.All(windows, w => Assert.True(w.To - w.From <= TimeSpan.FromDays(31)));
    }

    [Fact]
    public void KeepsShortRangesAsASingleWindow()
    {
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

        var windows = PayPalPaymentsClient.SplitDateRange(from, to).ToList();

        Assert.Single(windows);
        Assert.Equal(from, windows[0].From);
        Assert.Equal(to, windows[0].To);
    }
}
