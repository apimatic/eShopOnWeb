using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment NewAuthorizedPayment(decimal amount = 100m) =>
        new("PPORDER1", "AUTH1", "CREATED", "USD", amount, DateTimeOffset.UtcNow.AddDays(29));

    private static Payment NewCapturedPayment(decimal amount = 100m)
    {
        var p = NewAuthorizedPayment(amount);
        p.RecordCapture("CAP1", "COMPLETED", amount, 3.5m, amount - 3.5m);
        return p;
    }

    [Fact]
    public void NewPaymentIsAuthorized()
    {
        var p = NewAuthorizedPayment();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void CaptureRecordsFeeAndNetAndBecomesRefundable()
    {
        var p = NewCapturedPayment(100m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3.5m, p.PayPalFee);
        Assert.Equal(96.5m, p.NetAmount);
        Assert.Equal(100m, p.RefundableRemaining());
    }

    [Fact]
    public void CannotCaptureTwice()
    {
        var p = NewCapturedPayment();
        Assert.Throws<InvalidOperationException>(() => p.RecordCapture("CAP2", "COMPLETED", 100m, 0m, 100m));
    }

    [Fact]
    public void PartialRefundLeavesRemainderRefundable()
    {
        var p = NewCapturedPayment(100m);
        p.AddRefund("REF1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded());
        Assert.Equal(60m, p.RefundableRemaining());
    }

    [Fact]
    public void TwoDistinctPartialRefundsAreAllowedUpToCaptured()
    {
        var p = NewCapturedPayment(100m);
        p.AddRefund("REF1", 40m, "COMPLETED", "key-1");
        p.AddRefund("REF2", 60m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var p = NewCapturedPayment(100m);
        p.AddRefund("REF1", 90m, "COMPLETED", "key-1");

        // Only 10 remains; a further 20 must never be allowed.
        Assert.Throws<InvalidOperationException>(() => p.AddRefund("REF2", 20m, "COMPLETED", "key-2"));
        Assert.Equal(10m, p.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByKeyEnablesIdempotentReplay()
    {
        var p = NewCapturedPayment(100m);
        var first = p.AddRefund("REF1", 25m, "COMPLETED", "key-1");

        var found = p.FindRefundByKey("key-1");
        Assert.Same(first, found);
        Assert.Null(p.FindRefundByKey("other-key"));
    }

    [Fact]
    public void VoidReleasesAuthorizationAndBlocksCapture()
    {
        var p = NewAuthorizedPayment();
        p.Void();

        Assert.Equal(PaymentStatus.Voided, p.Status);
        Assert.Throws<InvalidOperationException>(() => p.RecordCapture("CAP1", "COMPLETED", 100m, 0m, 100m));
    }

    [Fact]
    public void ReauthorizationReplacesAuthorizationId()
    {
        var p = NewAuthorizedPayment();
        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
        p.RecordReauthorization("AUTH2", "CREATED", newExpiry);

        Assert.Equal("AUTH2", p.AuthorizationId);
        Assert.Equal(newExpiry, p.AuthorizationExpiresAt);
    }
}
