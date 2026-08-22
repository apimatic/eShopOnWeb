using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(0m, order.RefundedAmount());
    }

    [Fact]
    public void AuthorizeThenFulfilTransitions()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Payment.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, order.Total(), "USD");
        order.MarkAuthorized();
        order.Payment.RecordCapture("CAP-1", "COMPLETED", order.Total(), 0.30m, order.Total() - 0.30m);
        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.Payment.CaptureId);
        Assert.Equal(0.30m, order.Payment.PaypalFee);
    }

    [Fact]
    public void CancelAfterFulfilThrows()
    {
        var order = FulfilledOrder();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void PartialRefundThenFullRefund()
    {
        var order = FulfilledOrder();
        var first = order.AddRefund("R1", "COMPLETED", 1.00m, "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(1.00m, order.RefundedAmount());

        var second = order.AddRefund("R2", "COMPLETED", order.RemainingRefundableAmount(), "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundableAmount());
        Assert.Equal(first.PayPalRefundId, order.FindRefundByIdempotencyKey("key-1")!.PayPalRefundId);
        Assert.Equal(second.PayPalRefundId, order.FindRefundByIdempotencyKey("key-2")!.PayPalRefundId);
    }

    [Fact]
    public void RefundBeyondCapturedAmountThrows()
    {
        var order = FulfilledOrder();
        order.AddRefund("R1", "COMPLETED", 1.00m, "key-1");

        Assert.Throws<InvalidOrderStateException>(() =>
            order.AddRefund("R2", "COMPLETED", order.Total(), "key-2"));
    }

    [Fact]
    public void AuthorizeRequestIdIsStable()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var first = order.Payment.EnsureAuthorizeRequestId(42);
        var second = order.Payment.EnsureAuthorizeRequestId(42);

        Assert.Equal("eshop-auth-42", first);
        Assert.Equal(first, second);
    }

    private static Order FulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Payment.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, order.Total(), "USD");
        order.MarkAuthorized();
        order.Payment.RecordCapture("CAP-1", "COMPLETED", order.Total(), 0.30m, order.Total() - 0.30m);
        order.MarkFulfilled();
        return order;
    }
}
