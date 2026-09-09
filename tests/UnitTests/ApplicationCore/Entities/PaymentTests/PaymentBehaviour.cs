using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehaviour
{
    private static Payment NewAuthorized(decimal amount = 100m) =>
        new("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), amount, "USD", "INV-1");

    private static Payment NewCaptured(decimal amount = 100m, decimal gross = 100m)
    {
        var p = NewAuthorized(amount);
        p.MarkCaptured("CAP-1", "COMPLETED", gross, 3.2m, gross - 3.2m);
        return p;
    }

    [Fact]
    public void StartsAuthorizedWithNoRefunds()
    {
        var p = NewAuthorized();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Empty(p.Refunds);
        Assert.Equal(0m, p.TotalRefunded());
    }

    [Fact]
    public void CaptureRecordsPayPalAmounts()
    {
        var p = NewCaptured(gross: 100m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("CAP-1", p.CaptureId);
        Assert.Equal(100m, p.CapturedGross);
        Assert.Equal(3.2m, p.PayPalFee);
        Assert.Equal(96.8m, p.NetAmount);
        Assert.Equal(100m, p.RefundableRemaining());
    }

    [Fact]
    public void CannotVoidACapturedPayment()
    {
        var p = NewCaptured();
        Assert.Throws<InvalidOperationException>(() => p.MarkVoided());
    }

    [Fact]
    public void CannotCaptureTwice()
    {
        var p = NewCaptured();
        Assert.Throws<InvalidOperationException>(() => p.MarkCaptured("CAP-2", "COMPLETED", 100m, 0m, 100m));
    }

    [Fact]
    public void PartialRefundLeavesRemainderAndMarksPartiallyRefunded()
    {
        var p = NewCaptured(gross: 100m);
        p.AddRefund(new Refund("R1", "key-1", 40m, "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded());
        Assert.Equal(60m, p.RefundableRemaining());
    }

    [Fact]
    public void SecondPartialRefundThatUsesTheRemainderMarksFullyRefunded()
    {
        var p = NewCaptured(gross: 100m);
        p.AddRefund(new Refund("R1", "key-1", 40m, "COMPLETED"));
        p.AddRefund(new Refund("R2", "key-2", 60m, "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var p = NewCaptured(gross: 100m);
        p.AddRefund(new Refund("R1", "key-1", 90m, "COMPLETED"));

        // Only 10 remains; a 20 refund must never make the order refundable beyond what was captured.
        Assert.Throws<InvalidOperationException>(() =>
            p.AddRefund(new Refund("R2", "key-2", 20m, "COMPLETED")));
        Assert.Equal(10m, p.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByKeyReturnsTheOriginalRefund()
    {
        var p = NewCaptured();
        var refund = new Refund("R1", "key-1", 25m, "COMPLETED");
        p.AddRefund(refund);

        Assert.Same(refund, p.FindRefundByKey("key-1"));
        Assert.Null(p.FindRefundByKey("other-key"));
    }

    [Fact]
    public void RenewAuthorizationReplacesTheHold()
    {
        var p = NewAuthorized();
        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
        p.RenewAuthorization("AUTH-2", "CREATED", newExpiry);

        Assert.Equal("AUTH-2", p.AuthorizationId);
        Assert.Equal(newExpiry, p.AuthorizationExpiresAt);
        Assert.Equal(PaymentStatus.Authorized, p.Status);
    }
}
