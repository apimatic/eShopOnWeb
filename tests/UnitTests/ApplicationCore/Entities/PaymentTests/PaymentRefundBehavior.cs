using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundBehavior
{
    private static Payment CapturedPayment(decimal gross = 29m)
    {
        var payment = new Payment("ESHOP-run-1", gross, "USD", "PPO-1", "auth-run-1");
        payment.SetAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        payment.SetCapture("CAP-1", "COMPLETED", gross, 1.24m, gross - 1.24m);
        return payment;
    }

    [Fact]
    public void Captured_payment_is_fully_refundable()
    {
        var payment = CapturedPayment(29m);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(29m, payment.RefundableRemaining);
        Assert.Equal(0m, payment.TotalRefunded);
    }

    [Fact]
    public void Partial_refund_reduces_remaining_and_sets_partially_refunded()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund("R1", 10m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.TotalRefunded);
        Assert.Equal(19m, payment.RefundableRemaining);
    }

    [Fact]
    public void Two_partial_refunds_that_sum_to_the_capture_mark_it_refunded()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund("R1", 10m, "COMPLETED", "key-1");
        payment.AddRefund("R2", 19m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void Failed_refund_does_not_reduce_the_refundable_balance()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund("R1", 10m, "FAILED", "key-1");

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(29m, payment.RefundableRemaining);
    }

    [Fact]
    public void Refund_can_be_found_by_idempotency_key()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund("R1", 10m, "COMPLETED", "key-1");

        Assert.NotNull(payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }
}
