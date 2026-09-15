using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(3.69m, order.RoundedTotal());
    }

    [Fact]
    public void AuthorizeThenFulfilThenPartialRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.11m, order.PaypalFee);
        Assert.Equal(3.58m, order.NetAmount);

        order.AddRefund("REFUND-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RefundableRemaining());

        var duplicate = order.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(duplicate);

        Assert.Throws<InvalidOperationException>(() =>
            order.AddRefund("REFUND-2", "COMPLETED", 9.00m, "USD", "key-2"));

        order.AddRefund("REFUND-2", "COMPLETED", 2.69m, "USD", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void CancelReleasesAuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
    }
}
