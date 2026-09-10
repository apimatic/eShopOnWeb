using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

/// <summary>
/// The money rules that must hold regardless of what PayPal returns: a payment can only be refunded
/// once captured, never beyond what was captured, and repeated keys are recognisable so a request is
/// not applied twice.
/// </summary>
public class PaymentRefundRules
{
    private static Payment Captured(decimal amount = 100m)
    {
        var p = new Payment(1, "buyer-1", amount, "USD", "PP-ORDER-1", "eshop-run-1");
        p.SetAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "1111", "VISA");
        p.SetCapture("CAP-1", "COMPLETED", amount, 3.20m, amount - 3.20m);
        return p;
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var p = new Payment(1, "buyer-1", 50m, "USD", "PP-ORDER-1", "eshop-run-1");
        p.SetAuthorization("AUTH-1", "CREATED", null, "1111", "VISA");
        Assert.Throws<InvalidOperationException>(() => p.AddRefund("R1", 10m, "COMPLETED", "k1"));
    }

    [Fact]
    public void RefundsReduceRefundableRemaining()
    {
        var p = Captured(100m);
        p.AddRefund("R1", 30m, "COMPLETED", "k1");
        Assert.Equal(70m, p.RefundableRemaining());
        p.AddRefund("R2", 20m, "COMPLETED", "k2");
        Assert.Equal(50m, p.RefundableRemaining());
    }

    [Fact]
    public void CannotRefundBeyondCapturedAmountAcrossMultipleRefunds()
    {
        var p = Captured(100m);
        p.AddRefund("R1", 60m, "COMPLETED", "k1");
        Assert.Throws<InvalidOperationException>(() => p.AddRefund("R2", 50m, "COMPLETED", "k2"));
    }

    [Fact]
    public void FullRefundLeavesNothingRefundableAndIsFullyRefunded()
    {
        var p = Captured(100m);
        p.AddRefund("R1", 100m, "COMPLETED", "k1");
        Assert.Equal(0m, p.RefundableRemaining());
        Assert.True(p.IsFullyRefunded());
    }

    [Fact]
    public void SameIdempotencyKeyIsRecognised()
    {
        var p = Captured(100m);
        var r = p.AddRefund("R1", 10m, "COMPLETED", "dup-key");
        Assert.Same(r, p.FindRefundByIdempotencyKey("dup-key"));
        Assert.Null(p.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void FailedRefundsDoNotConsumeRefundableAmount()
    {
        var p = Captured(100m);
        p.AddRefund("R1", 100m, "FAILED", "k1");
        Assert.Equal(100m, p.RefundableRemaining());
    }
}
