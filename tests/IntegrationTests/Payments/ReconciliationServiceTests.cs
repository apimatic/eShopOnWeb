using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class ReconciliationServiceTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Report_CoversEveryPageOfEveryWindow_AndLinesUpBothSides()
    {
        // An eShop order that was paid, captured and partly refunded.
        var orderId = await _host.PlaceOrderAsync();
        var payment = await _host.InScopeAsync(async sp =>
        {
            var svc = sp.GetRequiredService<PaymentService>();
            await svc.PayAsync(orderId, PaymentTestHost.Buyer, new PayOrderCommand(PaymentServiceTests.TestCard(), null), CancellationToken.None);
            return await svc.FulfilAsync(orderId, CancellationToken.None);
        });
        var (_, refund) = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>()
            .RefundAsync(orderId, PaymentTestHost.Buyer, 5m, "r1", CancellationToken.None));

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-50);
        // PayPal's record: our capture (matched), 1,200 unrelated transactions spread over two 31-day windows
        // (three pages in one window), and nothing for our refund (eShop-only).
        _host.PayPal.ReportTransactions.Add(FakePayPal.Transaction(payment.CaptureId!, now.AddMinutes(-1), 51m));
        for (var i = 0; i < 1200; i++)
            _host.PayPal.ReportTransactions.Add(FakePayPal.Transaction($"OTHER{i:D6}", i < 1100 ? now.AddDays(-2) : now.AddDays(-40), 1m));

        var report = await _host.InScopeAsync(sp => sp.GetRequiredService<ReconciliationService>().BuildAsync(from, now.AddMinutes(5), CancellationToken.None));

        Assert.True(report.Complete);
        Assert.Equal(1201, report.PayPalTransactionCount);
        var matched = Assert.Single(report.Matched);
        Assert.Equal(orderId, matched.OrderId);
        Assert.Equal("capture", matched.MatchedAs);
        Assert.Equal(1200, report.PayPalOnly.Count);
        var eShopOnly = Assert.Single(report.EShopOnly);
        Assert.Equal("refund", eShopOnly.Kind);
        Assert.Equal(refund.PayPalRefundId, eShopOnly.Reference);

        var searches = _host.PayPal.Requests.Where(r => r.Path.StartsWith("/v1/reporting/transactions")).ToList();
        Assert.Contains(searches, s => s.Path.Contains("page=3"));
        Assert.True(searches.Count >= 4); // two windows, three pages in the busy one
    }

    [Fact]
    public async Task Report_RangeLongerThanAYear_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => _host.InScopeAsync(sp =>
            sp.GetRequiredService<ReconciliationService>().BuildAsync(DateTimeOffset.UtcNow.AddDays(-400), DateTimeOffset.UtcNow, CancellationToken.None)));

        Assert.Equal("range_too_large", ex.Code);
    }

    [Fact]
    public async Task Report_EmptyRange_IsCompleteAndEmpty()
    {
        var report = await _host.InScopeAsync(sp => sp.GetRequiredService<ReconciliationService>()
            .BuildAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.True(report.Complete);
        Assert.Empty(report.Matched);
        Assert.Empty(report.PayPalOnly);
    }
}
