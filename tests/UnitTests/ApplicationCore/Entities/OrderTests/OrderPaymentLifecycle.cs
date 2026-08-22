using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.False(order.Payment.HasHold);
    }

    [Fact]
    public void AuthorizeThenFulfilThenRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "COMPLETED");
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m, "capture-key", "CAPTURED");
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.Payment.CapturedAmount);
        Assert.Equal(0.11m, order.Payment.PaypalFee);
        Assert.Equal(3.58m, order.Payment.NetAmount);

        var first = order.AddRefund("R-1", "key-1", 1.00m, "COMPLETED", "PARTIALLY_REFUNDED");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.Payment.RemainingRefundable);
        Assert.Equal("R-1", first.PaypalRefundId);

        Assert.Same(first, order.FindRefundByIdempotencyKey("key-1"));

        order.AddRefund("R-2", "key-2", 2.69m, "COMPLETED", "REFUNDED");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment.RemainingRefundable);
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("AUTH-1", "CREATED", null, null);
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m, "capture-key", "CAPTURED");

        Assert.Throws<InvalidOperationException>(() =>
            order.AddRefund("R-1", "key-1", 4.00m, "COMPLETED", null));
    }

    [Fact]
    public void CancelReleasesBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("AUTH-1", "CREATED", null, null);
        order.MarkCancelled("VOIDED", "void-key");
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<InvalidOperationException>(() =>
            order.MarkFulfilled("CAP-1", "COMPLETED", 1m, 0m, 1m, "c", null));
    }
}
