using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void StartsAwaitingPaymentAndUsesCatalogTotal()
    {
        var order = BuildOrder();

        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Unfulfilled, order.FulfilmentStatus);
        Assert.Equal(25.00m, order.Total());
        Assert.Equal("USD", order.Currency);
        Assert.StartsWith("eshop-", order.PaymentReference);
    }

    [Fact]
    public void RejectsAuthorizationForWrongAmount()
    {
        var order = BuildOrder();

        Assert.Throws<InvalidOperationException>(() => order.RecordAuthorization("auth", "CREATED",
            24.99m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111"));
    }

    [Fact]
    public void CapturesAndBoundsIdempotentPartialRefunds()
    {
        var order = BuildOrder();
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("auth", "CREATED", 25m, now, now.AddDays(29), "VISA", "1111");
        order.RecordCapture("capture", "COMPLETED", 25m, 1m, 24m, now);

        var first = order.AddRefund("return-1", "request-1", "refund-1", "COMPLETED", 10m, now);
        var replay = order.AddRefund("return-1", "request-1", "refund-1", "COMPLETED", 10m, now);

        Assert.Same(first, replay);
        Assert.Equal(10m, order.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Throws<InvalidOperationException>(() => order.AddRefund("return-2", "request-2",
            "refund-2", "COMPLETED", 15.01m, now));

        order.AddRefund("return-2", "request-2", "refund-2", "COMPLETED", 15m, now);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(2, order.Refunds.Count);
    }

    private static Order BuildOrder() => new("buyer",
        new Address("1 Main St", "Seattle", "WA", "United States", "98101"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Test item", "test.png"), 12.50m, 2)
        }, "usd");
}
