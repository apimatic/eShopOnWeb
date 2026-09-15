using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundRules
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@example.com", amount: amount, currencyCode: "USD", payPalOrderId: "PPO-1");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        payment.SetCaptured("CAP-1", "COMPLETED", amount, payPalFee: 3m, netAmount: amount - 3m);
        return payment;
    }

    [Fact]
    public void Partial_refund_marks_partially_refunded_and_reduces_remaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF-1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded());
        Assert.Equal(60m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refunding_the_whole_captured_amount_marks_refunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.True(payment.IsFullyRefunded);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void Two_distinct_partial_refunds_are_both_recorded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("REF-2", 25m, "COMPLETED", "key-2");

        Assert.Equal(65m, payment.TotalRefunded());
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void Refund_beyond_captured_amount_is_rejected()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("REF-1", 80m, "COMPLETED", "key-1");

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("REF-2", 30m, "COMPLETED", "key-2"));
    }

    [Fact]
    public void Cannot_refund_before_capture()
    {
        var payment = new Payment(1, "buyer@example.com", 50m, "USD", "PPO-1");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void Refund_lookup_by_idempotency_key_returns_the_prior_refund()
    {
        var payment = CapturedPayment(100m);
        var recorded = payment.AddRefund("REF-1", 40m, "COMPLETED", "key-1");

        var found = payment.FindRefundByIdempotencyKey("key-1");

        Assert.NotNull(found);
        Assert.Equal(recorded.RefundId, found!.RefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void Failed_refund_does_not_count_against_the_captured_total()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF-1", 40m, "FAILED", "key-1");

        Assert.Equal(0m, payment.TotalRefunded());
        Assert.Equal(100m, payment.RefundableRemaining());
    }
}
