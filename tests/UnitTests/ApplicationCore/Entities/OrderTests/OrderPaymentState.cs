using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Order Authorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED",
            DateTimeOffset.UtcNow.AddDays(3), "USD", "VISA ****1111");
        return order;
    }

    [Fact]
    public void NewOrderAwaitsPaymentAndHasAReference()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.False(string.IsNullOrEmpty(order.PaymentReference));
    }

    [Fact]
    public void EachOrderGetsADistinctPaymentReference()
    {
        var a = new OrderBuilder().WithDefaultValues();
        var b = new OrderBuilder().WithDefaultValues();

        Assert.NotEqual(a.PaymentReference, b.PaymentReference);
    }

    [Fact]
    public void MarkAuthorizedRecordsTheHold()
    {
        var order = Authorized();

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("PAYPAL-ORDER", order.PayPalOrderId);
        Assert.Equal("AUTH-1", order.AuthorizationId);
        Assert.Equal("USD", order.PaymentCurrency);
    }

    [Fact]
    public void CannotCaptureBeforeAuthorizing()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<OrderPaymentException>(() =>
            order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m));
    }

    [Fact]
    public void MarkCapturedRecordsTheFeeBreakdown()
    {
        var order = Authorized();

        order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m);

        Assert.Equal(OrderPaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.20m, order.PayPalFee);
        Assert.Equal(3.49m, order.NetAmount);
    }

    [Fact]
    public void CancelReleasesTheHoldOnlyWhileAuthorized()
    {
        var order = Authorized();
        order.MarkCancelled();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.AuthorizationStatus);
        Assert.Throws<OrderPaymentException>(() => order.MarkCancelled());
    }

    [Fact]
    public void PartialRefundLeavesOrderPartiallyRefunded()
    {
        var order = Authorized();
        order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m);

        order.AddRefund(new OrderRefund("REF-1", 1.00m, "COMPLETED", "key-1"));

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(1.00m, order.RefundedAmount);
        Assert.Equal(2.69m, order.RefundableAmount);
    }

    [Fact]
    public void FullRefundLeavesOrderRefunded()
    {
        var order = Authorized();
        order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m);

        order.AddRefund(new OrderRefund("REF-1", 3.69m, "COMPLETED", "key-1"));

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableAmount);
    }

    [Fact]
    public void RefundsCannotExceedTheCapturedAmount()
    {
        var order = Authorized();
        order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m);
        order.AddRefund(new OrderRefund("REF-1", 3.00m, "COMPLETED", "key-1"));

        Assert.Throws<OrderPaymentException>(() =>
            order.AddRefund(new OrderRefund("REF-2", 1.00m, "COMPLETED", "key-2")));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsTheRecordedRefund()
    {
        var order = Authorized();
        order.MarkCaptured("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m);
        order.AddRefund(new OrderRefund("REF-1", 1.00m, "COMPLETED", "key-1"));

        Assert.NotNull(order.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(order.FindRefundByIdempotencyKey("other"));
    }
}
