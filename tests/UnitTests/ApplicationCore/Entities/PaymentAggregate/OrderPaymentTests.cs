using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentAggregate;

public class OrderPaymentTests
{
    private static OrderPayment NewAuthorizedThenCaptured(decimal amount = 100m)
    {
        var payment = new OrderPayment(orderId: 1, buyerId: "buyer@x.com", authorizedAmount: amount, currency: "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", DateTimeOffset.UtcNow);
        payment.MarkFulfilled("CAP1", amount, 3.20m, amount - 3.20m, DateTimeOffset.UtcNow);
        return payment;
    }

    [Fact]
    public void New_payment_starts_pending_with_a_unique_idempotency_seed()
    {
        var a = new OrderPayment(1, "b", 10m, "USD");
        var b = new OrderPayment(2, "b", 10m, "USD");

        Assert.Equal(PaymentStatus.PendingPayment, a.Status);
        Assert.False(string.IsNullOrWhiteSpace(a.IdempotencySeed));
        Assert.NotEqual(a.IdempotencySeed, b.IdempotencySeed);
    }

    [Fact]
    public void MarkAuthorized_then_MarkFulfilled_record_provider_state()
    {
        var payment = NewAuthorizedThenCaptured(50m);

        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(50m, payment.CapturedGross);
        Assert.Equal(3.20m, payment.PayPalFee);
        Assert.Equal(46.80m, payment.NetAmount);
    }

    [Fact]
    public void Partial_refund_within_capture_is_accepted_and_marks_partially_refunded()
    {
        var payment = NewAuthorizedThenCaptured(100m);

        var refund = payment.AddRefund("key-1", 40m);
        refund.Settle("R1", RefundState.Completed);
        payment.RecomputeRefundStatus();

        Assert.Equal(40m, payment.TotalRefunded());
        Assert.Equal(60m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void Refunds_totalling_the_capture_mark_fully_refunded()
    {
        var payment = NewAuthorizedThenCaptured(100m);

        payment.AddRefund("k1", 60m).Settle("R1", RefundState.Completed);
        payment.AddRefund("k2", 40m).Settle("R2", RefundState.Completed);
        payment.RecomputeRefundStatus();

        Assert.Equal(0m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void Refund_exceeding_remaining_is_rejected()
    {
        var payment = NewAuthorizedThenCaptured(100m);
        payment.AddRefund("k1", 80m).Settle("R1", RefundState.Completed);

        // 30 more would exceed the 20 remaining.
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("k2", 30m));
    }

    [Fact]
    public void Failed_refund_does_not_consume_refundable_amount()
    {
        var payment = NewAuthorizedThenCaptured(100m);
        var refund = payment.AddRefund("k1", 100m);
        refund.Settle(null, RefundState.Failed);
        payment.RecomputeRefundStatus();

        // A failed refund is not counted, so the full amount remains refundable.
        Assert.Equal(100m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
    }

    [Fact]
    public void Cancel_sets_cancelled_state()
    {
        var payment = new OrderPayment(1, "b", 10m, "USD");
        payment.MarkAuthorized("O", "A", DateTimeOffset.UtcNow);
        payment.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }
}
