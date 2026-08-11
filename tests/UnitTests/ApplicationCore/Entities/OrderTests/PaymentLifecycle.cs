using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

/// <summary>Money-safety invariants of the Order aggregate's payment lifecycle.</summary>
public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 47.50m) =>
        new("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), amount, "USD", "Card ending 1111");

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void MarkAuthorizedMovesToAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.NotNull(order.Payment);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        Assert.Throws<InvalidOperationException>(() => order.MarkAuthorized(NewPayment()));
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<InvalidOperationException>(() =>
            order.MarkFulfilled("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m));
    }

    [Fact]
    public void FulfilRecordsCaptureEconomics()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        order.MarkFulfilled("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(47.50m, order.Payment.CapturedGross);
        Assert.Equal(1.72m, order.Payment.PayPalFee);
        Assert.Equal(45.78m, order.Payment.NetAmount);
        Assert.Equal(47.50m, order.Payment.RefundableRemaining);
    }

    [Fact]
    public void CancelBeforeFulfilmentVoidsHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkFulfilled("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void PartialThenFullRefundTracksRemainingAndStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkFulfilled("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        order.AddRefund("REF-1", 10m, "COMPLETED", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(10m, order.Payment!.TotalRefunded);
        Assert.Equal(37.50m, order.Payment.RefundableRemaining);

        order.AddRefund("REF-2", 37.50m, "COMPLETED", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment.RefundableRemaining);
    }

    [Fact]
    public void RefundCannotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());
        order.MarkFulfilled("CAP-1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        Assert.Throws<InvalidOperationException>(() =>
            order.AddRefund("REF-1", 100m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void CannotRefundBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(NewPayment());

        Assert.Throws<InvalidOperationException>(() =>
            order.AddRefund("REF-1", 10m, "COMPLETED", "key-1"));
    }
}
