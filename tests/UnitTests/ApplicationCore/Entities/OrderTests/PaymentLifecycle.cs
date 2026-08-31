using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void RecordsAuthorizationCaptureAndRefundState()
    {
        var order = BuildOrder(10m);
        var now = DateTimeOffset.UtcNow;

        order.RecordAuthorization("paypal-order", "authorization", "CREATED", 10m, "USD",
            now, now, now.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", 10m, "USD", 0.80m, 9.20m, now);
        order.MarkFulfilled(now);
        order.AddRefund(new PaymentRefund("refund-1", "key-1", 4m, "USD", "COMPLETED", now));

        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, order.FulfilmentStatus);
        Assert.Equal(4m, order.RefundedAmount());
        Assert.Equal(0.80m, order.PayPalFee);
        Assert.Equal(9.20m, order.NetProceeds);
    }

    [Fact]
    public void RepeatedRefundKeyDoesNotCountTwice()
    {
        var order = BuildOrder(10m);
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("paypal-order", "authorization", "CREATED", 10m, "USD",
            now, now, now.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", 10m, "USD", 0.80m, 9.20m, now);
        order.AddRefund(new PaymentRefund("refund-1", "same-key", 5m, "USD", "COMPLETED", now));
        order.AddRefund(new PaymentRefund("refund-1", "same-key", 5m, "USD", "COMPLETED", now));

        Assert.Single(order.Refunds);
        Assert.Equal(5m, order.RefundedAmount());
    }

    [Fact]
    public void RejectsAuthorizationForWrongAmount()
    {
        var order = BuildOrder(10m);

        Assert.Throws<InvalidOperationException>(() => order.RecordAuthorization(
            "paypal-order", "authorization", "CREATED", 9.99m, "USD",
            DateTimeOffset.UtcNow, null, null));
    }

    private static Order BuildOrder(decimal price)
    {
        var builder = new OrderBuilder();
        return builder.WithItems(new List<OrderItem>
        {
            new(builder.TestCatalogItemOrdered, price, 1)
        });
    }
}
