using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void AuthorizeThenFulfilThenPartialAndFullRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPALORDER", "COMPLETED", "AUTH1", "CREATED", null, "USD");
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);

        order.MarkFulfilled("CAP1", "COMPLETED", 8.5m, 0.30m, 8.20m, "CAPTURED");
        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(8.5m, order.CapturedAmount);
        Assert.Equal(0.30m, order.PaypalFee);
        Assert.Equal(8.20m, order.NetAmount);

        var first = order.AddRefund("R1", "COMPLETED", "key-1", 3m);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(5.5m, order.RemainingRefundable());
        Assert.Equal("key-1", first.IdempotencyKey);

        order.AddRefund("R2", "COMPLETED", "key-2", 5.5m);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPALORDER", "COMPLETED", "AUTH1", "CREATED", null, "USD");
        order.MarkFulfilled("CAP1", "COMPLETED", 8.5m, 0.30m, 8.20m);

        Assert.Throws<PaymentException>(() => order.AddRefund("R1", "COMPLETED", "key-1", 9m));
    }

    [Fact]
    public void CancelReleasesAuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPALORDER", "COMPLETED", "AUTH1", "CREATED", null, "USD");
        order.MarkCancelled();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPALORDER", "COMPLETED", "AUTH1", "CREATED", null, "USD");
        order.MarkFulfilled("CAP1", "COMPLETED", 8.5m, 0.30m, 8.20m);

        Assert.Throws<PaymentException>(() => order.MarkCancelled());
    }

    [Fact]
    public void IdempotentFulfilmentIsANoOp()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPALORDER", "COMPLETED", "AUTH1", "CREATED", null, "USD");
        order.MarkFulfilled("CAP1", "COMPLETED", 8.5m, 0.30m, 8.20m);
        order.MarkFulfilled("CAP2", "COMPLETED", 99m, 1m, 98m);

        Assert.Equal("CAP1", order.PayPalCaptureId);
        Assert.Equal(8.5m, order.CapturedAmount);
    }
}
