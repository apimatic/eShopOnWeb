using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundBehavior
{
    private static Payment CapturedPayment(decimal amount = 29m)
    {
        var payment = new Payment(1, "buyer@example.com", amount, "USD", "INV-1");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return payment;
    }

    [Fact]
    public void RepeatingAnIdempotencyKeyReturnsTheSameRefund()
    {
        var payment = CapturedPayment();

        var first = payment.AddRefund("key-1", 5m);
        var second = payment.AddRefund("key-1", 5m);

        Assert.Same(first, second);
        Assert.Single(payment.Refunds);
        Assert.Equal(5m, payment.RefundedTotal());
    }

    [Fact]
    public void DistinctKeysProduceDistinctPartialRefunds()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-1", 5m);
        payment.AddRefund("key-2", 3m);

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(8m, payment.RefundedTotal());
        Assert.Equal(21m, payment.RefundableRemaining());
    }

    [Fact]
    public void RefundingBeyondCapturedAmountThrows()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund("key-1", 20m);

        var ex = Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-2", 15m));
        Assert.Contains("exceeds the refundable remaining", ex.Message);
    }

    [Fact]
    public void ConfirmingRefundsMovesStatusToPartiallyThenFullyRefunded()
    {
        var payment = CapturedPayment(10m);

        var r1 = payment.AddRefund("k1", 4m);
        payment.ConfirmRefund(r1, "RF-1", "COMPLETED");
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);

        var r2 = payment.AddRefund("k2", 6m);
        payment.ConfirmRefund(r2, "RF-2", "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void CannotRefundAnUncapturedPayment()
    {
        var payment = new Payment(1, "buyer@example.com", 10m, "USD", "INV-1");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("k1", 5m));
    }

    [Fact]
    public void DiscardingARejectedRefundRestoresState()
    {
        var payment = CapturedPayment(10m);
        var r = payment.AddRefund("k1", 4m);

        payment.DiscardRefund(r);

        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(10m, payment.RefundableRemaining());
    }
}
