using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static PayPalPayment NewPayment() =>
        new("PPORDER1", "AUTH1", "CREATED", "USD", "ESHOP-1-abc");

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void MarkAuthorizedSetsAuthorizedAndPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkAuthorized(NewPayment());

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void MarkCapturedRecordsFeeAndNetAndSetsRefundable()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        order.MarkCaptured("CAP1", "COMPLETED", capturedAmount: 29m, payPalFee: 1.24m, netAmount: 27.76m);

        Assert.Equal(OrderPaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal("CAP1", order.Payment!.CaptureId);
        Assert.Equal(1.24m, order.Payment.PayPalFee);
        Assert.Equal(27.76m, order.Payment.NetAmount);
        Assert.Equal(29m, order.RefundableRemaining());
    }

    [Fact]
    public void PartialThenFullRefundAdvancesStatusAndTracksRemaining()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkCaptured("CAP1", "COMPLETED", 29m, 1.24m, 27.76m);

        order.AddRefund("R1", 5m, "COMPLETED", "k1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(5m, order.TotalRefunded());
        Assert.Equal(24m, order.RefundableRemaining());

        order.AddRefund("R2", 24m, "COMPLETED", "k2");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkCaptured("CAP1", "COMPLETED", 29m, 1.24m, 27.76m);

        Assert.Throws<InvalidOperationException>(() => order.AddRefund("R1", 100m, "COMPLETED", "k1"));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkCaptured("CAP1", "COMPLETED", 29m, 1.24m, 27.76m);
        order.AddRefund("R1", 5m, "COMPLETED", "k1");

        Assert.Equal("R1", order.FindRefundByIdempotencyKey("k1")!.RefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("nope"));
    }

    [Fact]
    public void CancelSetsCancelledAndVoidsAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        order.MarkCancelled();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }
}
