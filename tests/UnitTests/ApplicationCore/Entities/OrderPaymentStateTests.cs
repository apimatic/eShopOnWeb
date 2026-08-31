using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderPaymentStateTests
{
    private static Order NewOrder() => new("shopper", new Address("street", "city", "state", "US", "12345"),
        new List<OrderItem> { new(new CatalogItemOrdered(1, "item", "picture"), 10m, 1) });

    [Fact]
    public void AuthorizationCaptureAndPartialRefundsTrackMoney()
    {
        var order = NewOrder();
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("order", "auth", "CREATED", "USD", now, now.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", 10m, .70m, 9.30m);
        order.RecordRefund(3m);

        Assert.Equal(PaymentState.PartiallyRefunded, order.PaymentState);
        Assert.Equal(3m, order.RefundedAmount);
        Assert.Throws<InvalidOperationException>(() => order.RecordRefund(8m));

        order.RecordRefund(7m);
        Assert.Equal(PaymentState.Refunded, order.PaymentState);
    }

    [Fact]
    public void CancelledAuthorizationCannotBeCaptured()
    {
        var order = NewOrder();
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("order", "auth", "CREATED", "USD", now, now.AddDays(29));
        order.RecordCancellation("VOIDED");

        Assert.Equal(PaymentState.Cancelled, order.PaymentState);
        Assert.Throws<InvalidOperationException>(() => order.RecordCapture("capture", "COMPLETED", 10m, 1m, 9m));
    }
}
