using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    private readonly OrderBuilder _builder = new();

    [Fact]
    public void NewOrderStartsPendingPayment()
    {
        var order = _builder.WithDefaultValues();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(0, order.Refunds.Count);
    }

    [Fact]
    public void AuthorizeThenCaptureThenPartialRefund()
    {
        var order = _builder.WithDefaultValues();
        var total = order.Total();

        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), total, "USD");
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.MarkFulfilled("CAPTURE-1", "COMPLETED", total, 0.59m, total - 0.59m);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(total, order.Payment.CapturedAmount);
        Assert.Equal(0.59m, order.Payment.PaypalFee);

        var first = order.AddRefund("REFUND-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(1.00m, first.Amount);
        Assert.Equal(total - 1.00m, order.RefundableRemaining());

        var replay = order.AddRefund("REFUND-1-AGAIN", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Same(first, replay);
        Assert.Single(order.Refunds);

        order.AddRefund("REFUND-2", "COMPLETED", total - 1.00m, "USD", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = _builder.WithDefaultValues();
        var total = order.Total();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, total, "USD");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", total, null, null);

        Assert.Throws<PaymentException>(() => order.AddRefund("REFUND-1", "COMPLETED", total + 1, "USD", "key-1"));
    }

    [Fact]
    public void CancelAfterCaptureIsRejected()
    {
        var order = _builder.WithDefaultValues();
        var total = order.Total();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, total, "USD");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", total, null, null);

        Assert.Throws<OrderPaymentStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void IdempotentCancelFromPending()
    {
        var order = _builder.WithDefaultValues();
        order.MarkCancelled(null);
        order.MarkCancelled(null);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
