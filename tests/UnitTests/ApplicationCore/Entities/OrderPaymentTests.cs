using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderPaymentTests
{
    private static OrderPayment FulfilledPayment(decimal amount = 20m)
    {
        var p = new OrderPayment(orderId: 1, buyerId: "buyer@x.com", currencyCode: "USD", amount: amount);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        p.MarkFulfilled("CAP1", "COMPLETED", capturedAmount: amount, paypalFee: 1m, netAmount: amount - 1m);
        return p;
    }

    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithUniqueSeed()
    {
        var a = new OrderPayment(1, "b", "USD", 10m);
        var b = new OrderPayment(2, "b", "USD", 10m);
        Assert.Equal(PaymentStatus.AwaitingPayment, a.Status);
        Assert.False(string.IsNullOrWhiteSpace(a.IdempotencySeed));
        Assert.NotEqual(a.IdempotencySeed, b.IdempotencySeed);
    }

    [Fact]
    public void MarkFulfilled_SetsCaptureFeeAndNet_AndMakesRefundable()
    {
        var p = FulfilledPayment(20m);
        Assert.Equal(PaymentStatus.Fulfilled, p.Status);
        Assert.Equal(20m, p.CapturedAmount);
        Assert.Equal(1m, p.PayPalFee);
        Assert.Equal(19m, p.NetAmount);
        Assert.Equal(20m, p.RefundableRemaining);
    }

    [Fact]
    public void PartialRefund_ThenFullRemaining_TransitionsStatuses()
    {
        var p = FulfilledPayment(20m);

        p.AddRefund(new PaymentRefund("k1", "R1", 5m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(5m, p.TotalRefunded);
        Assert.Equal(15m, p.RefundableRemaining);

        p.AddRefund(new PaymentRefund("k2", "R2", 15m, "COMPLETED"));
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining);
    }

    [Fact]
    public void AddRefund_BeyondCaptured_Throws()
    {
        var p = FulfilledPayment(20m);
        p.AddRefund(new PaymentRefund("k1", "R1", 12m, "COMPLETED"));
        Assert.False(p.CanRefund(9m));
        Assert.Throws<InvalidOperationException>(() => p.AddRefund(new PaymentRefund("k2", "R2", 9m, "COMPLETED")));
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsRecordedRefund()
    {
        var p = FulfilledPayment(20m);
        p.AddRefund(new PaymentRefund("dup-key", "R1", 5m, "COMPLETED"));
        Assert.NotNull(p.FindRefundByIdempotencyKey("dup-key"));
        Assert.Null(p.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void CanRefund_OnlyWhenCaptured()
    {
        var awaiting = new OrderPayment(1, "b", "USD", 20m);
        Assert.False(awaiting.CanRefund(5m));

        awaiting.MarkAuthorized("O", "A", "CREATED", null);
        Assert.False(awaiting.CanRefund(5m)); // authorized but not captured

        awaiting.MarkFulfilled("C", "COMPLETED", 20m, 1m, 19m);
        Assert.True(awaiting.CanRefund(5m));
    }

    [Fact]
    public void RenewAuthorization_ReplacesAuthorizationId()
    {
        var p = new OrderPayment(1, "b", "USD", 20m);
        p.MarkAuthorized("O", "AUTH_OLD", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-1));
        p.RenewAuthorization("AUTH_NEW", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        Assert.Equal("AUTH_NEW", p.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, p.Status);
    }
}
