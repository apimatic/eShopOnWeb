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

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void AuthorizeThenFulfilRecordsCaptureBreakdown()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD", "authorize-order-1");
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);

        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.21m, 3.48m, "capture-order-1");

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.21m, order.PaypalFee);
        Assert.Equal(3.48m, order.NetAmount);
        Assert.Equal(3.69m, order.RefundableRemaining());
    }

    [Fact]
    public void PartialRefundCannotExceedCapturedAmount()
    {
        var order = FulfilledOrder();

        order.RecordRefund("R1", "key-1", 1.00m, "COMPLETED");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RefundableRemaining());

        order.RecordRefund("R2", "key-2", 2.69m, "COMPLETED");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void RepeatingRefundIdempotencyKeyIsDiscoverable()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "same-key", 1.00m, "COMPLETED");

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("R1", found!.PayPalRefundId);
    }

    [Fact]
    public void CancelFromAuthorizedReleasesHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD", "authorize-order-1");
        order.MarkCancelled("void-order-1");

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
    }

    private static Order FulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD", "authorize-order-1");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.21m, 3.48m, "capture-order-1");
        return order;
    }
}
