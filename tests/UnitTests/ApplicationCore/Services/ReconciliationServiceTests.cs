using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ReconciliationServiceTests
{
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IReadRepository<Order> _orders = Substitute.For<IReadRepository<Order>>();

    private static Order Captured(string captureId)
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "W", "u"), 50m, 2) };
        var order = new Order("demo", new Address("s", "c", "st", "country", "zip"), items);
        order.StartPayment("USD");
        order.Payment!.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29));
        order.Payment!.MarkCaptured(captureId, "COMPLETED", 100m, 3m, 97m);
        return order;
    }

    [Fact]
    public async Task Lines_up_matched_paypal_only_and_eshop_only()
    {
        // eShop has captures CAP-1 and CAP-2.
        _orders.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order> { Captured("CAP-1"), Captured("CAP-2") });

        // PayPal reports CAP-1 (matches) and OTHER (no eShop order).
        _gateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<PayPalTransactionRecord>
            {
                new("CAP-1", "S", 100m, "USD", DateTimeOffset.Now),
                new("OTHER", "S", 5m, "USD", DateTimeOffset.Now)
            });

        var service = new ReconciliationService(_gateway, _orders);
        var report = await service.ReconcileAsync(DateTimeOffset.Now.AddDays(-30), DateTimeOffset.Now);

        Assert.Equal(1, report.MatchedCount);        // CAP-1
        Assert.Equal(1, report.InPayPalOnlyCount);   // OTHER
        Assert.Equal(1, report.InEShopOnlyCount);    // CAP-2 (PayPal report lags)

        Assert.Contains(report.Entries, e => e.Outcome == ReconciliationOutcome.Matched && e.PayPalTransactionId == "CAP-1");
        Assert.Contains(report.Entries, e => e.Outcome == ReconciliationOutcome.InPayPalOnly && e.PayPalTransactionId == "OTHER");
        Assert.Contains(report.Entries, e => e.Outcome == ReconciliationOutcome.InEShopOnly && e.PayPalTransactionId == "CAP-2");
    }

    [Fact]
    public async Task Rejects_inverted_range()
    {
        var service = new ReconciliationService(_gateway, _orders);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ReconcileAsync(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(-1)));
    }
}
