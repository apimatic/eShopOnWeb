using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.PayPal;

public class TransactionSearchPagingTests
{
    /// <summary>A page of two transactions, reporting <paramref name="totalPages"/> pages in total.</summary>
    private static string PageJson(int page, int totalPages) => $$"""
    {
      "transaction_details": [
        { "transaction_info": { "transaction_id": "TXN-{{page}}-A", "transaction_status": "S",
            "transaction_amount": { "currency_code": "USD", "value": "17.00" },
            "fee_amount": { "currency_code": "USD", "value": "-0.93" },
            "transaction_initiation_date": "2026-08-12T09:00:00+0000", "invoice_id": "eshop-1-x" } },
        { "transaction_info": { "transaction_id": "TXN-{{page}}-B", "transaction_status": "S",
            "transaction_amount": { "currency_code": "USD", "value": "12.00" },
            "transaction_initiation_date": "2026-08-13T09:00:00+0000" } }
      ],
      "page": {{page}},
      "total_items": 4,
      "total_pages": {{totalPages}},
      "last_refreshed_datetime": "2026-08-28T14:29:59+0000"
    }
    """;

    [Fact]
    public async Task ARangeLongerThanTheProvidersWindowIsWalkedWindowByWindow_AndEachWindowPagedToTheEnd()
    {
        // The provider caps a search at 31 days, so 60 days must become two windows; each window
        // reports two pages, so the whole range is four requests, not one.
        var handler = new StubHandler((request, _) =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var page = int.Parse(query["page"]!);
            return StubHandler.Json(HttpStatusCode.OK, PageJson(page, totalPages: 2));
        });

        var to = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var from = to.AddDays(-60);

        var result = await GatewayFactory.Create(handler).ListTransactionsAsync(from, to, default);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(8, result.Transactions.Count);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 14, 29, 59, TimeSpan.Zero), result.LastRefreshedAt);

        var windows = handler.Requests
            .Select(r => HttpUtility.ParseQueryString(r.RequestUri!.Query))
            .Select(q => (Start: q["start_date"]!, End: q["end_date"]!))
            .Distinct()
            .ToList();

        Assert.Equal(2, windows.Count);
        Assert.Equal("2026-06-29T00:00:00Z", windows[0].Start);
        Assert.Equal("2026-07-30T00:00:00Z", windows[0].End);
        // The second window picks up exactly where the first left off and stops at the requested end.
        Assert.Equal("2026-07-30T00:00:00Z", windows[1].Start);
        Assert.Equal("2026-08-28T00:00:00Z", windows[1].End);
    }

    [Fact]
    public async Task ARangeInsideOneWindowIsASingleRequest()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson(1, totalPages: 1));

        var to = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var result = await GatewayFactory.Create(handler).ListTransactionsAsync(to.AddDays(-7), to, default);

        Assert.Single(handler.Requests);
        Assert.Equal(2, result.Transactions.Count);

        var first = result.Transactions[0];
        Assert.Equal("TXN-1-A", first.TransactionId);
        Assert.Equal(17.00m, first.Amount);
        Assert.Equal(-0.93m, first.FeeAmount);
        Assert.Equal("eshop-1-x", first.InvoiceId);
    }

    [Fact]
    public async Task AnEmptyRangeIsAnEmptyReport_NotAFailure()
    {
        // Transaction reporting lags live activity, so a range covering only just-created payments
        // legitimately comes back empty.
        var handler = new StubHandler(HttpStatusCode.OK,
            """{ "transaction_details": [], "page": 1, "total_items": 0, "total_pages": 0 }""");

        var to = DateTimeOffset.UtcNow;
        var result = await GatewayFactory.Create(handler).ListTransactionsAsync(to.AddHours(-2), to, default);

        Assert.Empty(result.Transactions);
    }

    [Fact]
    public async Task ARangeBeyondTheProvidersRetentionIsRejectedBeforeAnyRequestGoesOut()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson(1, 1));

        var to = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            GatewayFactory.Create(handler).ListTransactionsAsync(to.AddYears(-5), to, default));

        Assert.Empty(handler.Requests);
    }
}
