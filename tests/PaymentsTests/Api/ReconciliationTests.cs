using System.Net;
using Microsoft.eShopWeb.PaymentsTests.Fakes;

namespace Microsoft.eShopWeb.PaymentsTests.Api;

public class ReconciliationTests
{
    [Fact]
    public async Task Report_walks_every_page_of_every_window_and_lines_PayPal_up_against_eShop()
    {
        using var factory = new PaymentsApiFactory();
        factory.PayPal.ReportingPageSizeOverride = 2; // force several pages per window
        var shopper = factory.ClientFor($"shopper-{Guid.NewGuid():N}@example.test");
        var admin = factory.ClientFor("admin@microsoft.com", "Administrators");

        var orderId = await shopper.PlaceOrderAsync((1, 2), (2, 1));
        await shopper.PostJsonAsync($"api/orders/{orderId}/pay", new { card = ApiCalls.Card() });
        var fulfilled = await (await admin.PostAsync($"api/orders/{orderId}/fulfil", null)).ReadJsonAsync();
        var captureId = fulfilled["order"]!["payment"]!["capture"]!["id"]!.GetValue<string>();
        var invoiceId = fulfilled["order"]!["payment"]!["invoiceId"]!.GetValue<string>();
        var refund = await (await shopper.PostJsonAsync($"api/orders/{orderId}/refunds", new { amount = 5m, idempotencyKey = "rec-1" })).ReadJsonAsync();
        var refundId = refund["payPalRefundId"]!.GetValue<string>();

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-70);
        // PayPal's record: our capture, a capture eShop does not know (same invoice), an unrelated
        // payment two months ago (a different 31-day window), and filler to force paging.
        factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction(captureId, now.AddMinutes(-1), 47.50m, invoiceId));
        factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction("UNKNOWN-TO-ESHOP", now.AddMinutes(-2), 12m, invoiceId));
        factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction("OLD-ELSEWHERE", from.AddDays(3), 99m));
        for (var i = 0; i < 5; i++)
            factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction($"FILLER-{i}", from.AddDays(40).AddMinutes(i), 1m));

        var response = await admin.GetAsync($"api/reconciliation?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(1).ToString("O"))}");
        var report = (await response.ReadJsonAsync())["report"]!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(report["isComplete"]!.GetValue<bool>());
        Assert.Equal(3, report["windows"]!.AsArray().Count); // 70+ days in windows of at most 31
        Assert.Contains(report["windows"]!.AsArray(), w => w!["pagesFetched"]!.GetValue<int>() >= 3);
        Assert.Equal(factory.PayPal.Calls("GET", "/v1/reporting/transactions").Count(), report["windows"]!.AsArray().Sum(w => w!["pagesFetched"]!.GetValue<int>()));

        var matched = Assert.Single(report["matched"]!.AsArray())!;
        Assert.Equal(captureId, matched["transactionId"]!.GetValue<string>());
        Assert.Equal(orderId, matched["orderId"]!.GetValue<int>());
        Assert.True(matched["amountsMatch"]!.GetValue<bool>());

        var paypalOnly = report["payPalOnly"]!.AsArray();
        Assert.Equal(7, paypalOnly.Count);
        var unknown = paypalOnly.Single(e => e!["transactionId"]!.GetValue<string>() == "UNKNOWN-TO-ESHOP")!;
        Assert.Equal(orderId, unknown["orderId"]!.GetValue<int>()); // traced back to the order by invoice id

        var eshopOnly = Assert.Single(report["eShopOnly"]!.AsArray())!;
        Assert.Equal(refundId, eshopOnly["transactionId"]!.GetValue<string>());
        Assert.Equal("refund", eshopOnly["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task Report_says_so_when_PayPal_stops_answering_part_way()
    {
        using var factory = new PaymentsApiFactory();
        factory.Resilience.RequestBudget = TimeSpan.FromSeconds(2);
        factory.PayPal.ReportingPageSizeOverride = 1;
        var now = DateTimeOffset.UtcNow;
        factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction("A", now.AddDays(-1), 1m));
        factory.PayPal.ReportingTransactions.Add(FakePayPal.ReportingTransaction("B", now.AddDays(-1).AddMinutes(1), 1m));
        var admin = factory.ClientFor("admin@microsoft.com", "Administrators");

        // The first page answers, then PayPal hangs.
        factory.PayPal.InjectFault("GET", "/v1/reporting/transactions", Fault.Hang, times: 5, passThrough: 1);
        var partial = await admin.GetAsync($"api/reconciliation?from={Uri.EscapeDataString(now.AddDays(-2).ToString("O"))}&to={Uri.EscapeDataString(now.ToString("O"))}");
        var report = (await partial.ReadJsonAsync())["report"]!;

        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        Assert.False(report["isComplete"]!.GetValue<bool>());
        Assert.Contains("did not respond", report["incompleteReason"]!.GetValue<string>());
        Assert.Single(report["payPalOnly"]!.AsArray()); // what was read before the cut-off is still reported
    }

    [Fact]
    public async Task Report_validates_its_range()
    {
        using var factory = new PaymentsApiFactory();
        var admin = factory.ClientFor("admin@microsoft.com", "Administrators");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("api/reconciliation?from=yesterday&to=2026-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("api/reconciliation?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")).StatusCode);
    }
}
