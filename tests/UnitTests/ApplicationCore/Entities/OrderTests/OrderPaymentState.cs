using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        Assert.Equal(3.69m, order.Total());
    }

    [Fact]
    public void RecordAuthorizationThenCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.15m, 3.54m);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.Payment.CapturedAmount);
        Assert.Equal(0.15m, order.Payment.PaypalFee);
        Assert.Equal(3.54m, order.Payment.NetAmount);
    }

    [Fact]
    public void CancelBeforeFulfilmentIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.MarkCancelled();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, null, null);
        Assert.Throws<PaymentException>(() => order.MarkCancelled());
    }

    [Fact]
    public void PartialRefundCannotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, null, null);

        var first = order.RecordRefund("R1", "key-1", 1.00m, "COMPLETED");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RefundableRemaining());

        Assert.Throws<PaymentException>(() => order.RecordRefund("R2", "key-2", 3.00m, "COMPLETED"));

        var second = order.RecordRefund("R3", "key-3", 2.69m, "COMPLETED");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
        Assert.Same(first, order.FindRefundByIdempotencyKey("key-1"));
        Assert.NotNull(second);
    }

    [Fact]
    public void IdempotentPayLeavesExistingAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordAuthorization("PP-ORDER-2", "AUTH-2", "CREATED", null, "USD");
        Assert.Equal("AUTH-2", order.Payment.AuthorizationId);
        Assert.Equal(OrderStatus.Authorized, order.Status);
    }
}
