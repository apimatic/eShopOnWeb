using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

/// <summary>
/// The money-safety invariants of <see cref="Payment"/>: a captured payment can be refunded in
/// part or in full, distinct partial refunds accumulate, and the total refunded can never exceed
/// what was captured. Refund idempotency keys make a repeat a no-op.
/// </summary>
public class PaymentRefundInvariants
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment("USD", amount);
        payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);
        payment.RecordCapture("CAP-1", "COMPLETED", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void CapturedPaymentIsFullyRefundable()
    {
        var payment = CapturedPayment(100m);
        Assert.Equal(100m, payment.RefundableAmount);
        Assert.Equal(0m, payment.TotalRefunded);
    }

    [Fact]
    public void PartialRefundsAccumulateAndReduceRefundable()
    {
        var payment = CapturedPayment(100m);

        var r1 = payment.AddRefund("key-1", 30m);
        r1.MarkAccepted("RF-1", "COMPLETED");
        payment.ApplyRefundSettlement();

        var r2 = payment.AddRefund("key-2", 20m);
        r2.MarkAccepted("RF-2", "COMPLETED");
        payment.ApplyRefundSettlement();

        Assert.Equal(50m, payment.TotalRefunded);
        Assert.Equal(50m, payment.RefundableAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void FullRefundMarksPaymentRefunded()
    {
        var payment = CapturedPayment(100m);
        var r = payment.AddRefund("key-1", 100m);
        r.MarkAccepted("RF-1", "COMPLETED");
        payment.ApplyRefundSettlement();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 60m).MarkAccepted("RF-1", "COMPLETED");
        payment.ApplyRefundSettlement();

        // The remaining refundable is 40; asking for 50 must be rejected.
        var ex = Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-2", 50m));
        Assert.Contains("exceeds the refundable balance", ex.Message);
    }

    [Fact]
    public void CannotRefundAnUncapturedPayment()
    {
        var payment = new Payment("USD", 100m);
        payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-1", 10m));
    }

    [Fact]
    public void FailedRefundDoesNotConsumeRefundableBalance()
    {
        var payment = CapturedPayment(100m);
        var r = payment.AddRefund("key-1", 40m);
        r.MarkFailed();

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(100m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundLookupByIdempotencyKeyFindsExisting()
    {
        var payment = CapturedPayment(100m);
        var r = payment.AddRefund("dedupe-key", 25m);

        Assert.Same(r, payment.FindRefundByKey("dedupe-key"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }
}
