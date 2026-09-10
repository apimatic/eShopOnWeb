using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundRules
{
    private static Payment CapturedPayment(decimal amount = 29.00m)
    {
        var p = new Payment(orderId: 1, buyerId: "buyer@x", amount: amount, currency: "USD");
        p.AssignInvoiceId("ESHOP-1-abc");
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        p.MarkCaptured("CAP1", "COMPLETED", amount, payPalFee: 1.24m, netAmount: amount - 1.24m);
        return p;
    }

    [Fact]
    public void Partial_refund_moves_status_to_PartiallyRefunded_and_reduces_remaining()
    {
        var p = CapturedPayment();
        p.AddRefund(new PaymentRefund("k1", "R1", 10m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10m, p.RefundedAmount);
        Assert.Equal(19m, p.RefundableRemaining);
    }

    [Fact]
    public void Refunding_the_full_captured_amount_moves_status_to_Refunded()
    {
        var p = CapturedPayment();
        p.AddRefund(new PaymentRefund("k1", "R1", 29m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining);
    }

    [Fact]
    public void Cumulative_refunds_may_never_exceed_the_captured_amount()
    {
        var p = CapturedPayment();
        p.AddRefund(new PaymentRefund("k1", "R1", 20m, "USD", "COMPLETED"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => p.AddRefund(new PaymentRefund("k2", "R2", 15m, "USD", "COMPLETED")));
        Assert.Contains("exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20m, p.RefundedAmount); // the rejected refund did not take effect
    }

    [Fact]
    public void Refund_is_rejected_before_capture()
    {
        var p = new Payment(1, "buyer@x", 29m, "USD");
        p.AssignInvoiceId("ESHOP-1-abc");
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);

        Assert.Throws<InvalidOperationException>(
            () => p.AddRefund(new PaymentRefund("k1", "R1", 1m, "USD", "COMPLETED")));
    }

    [Fact]
    public void TryGetExistingRefund_finds_a_refund_by_idempotency_key()
    {
        var p = CapturedPayment();
        p.AddRefund(new PaymentRefund("key-abc", "R1", 5m, "USD", "COMPLETED"));

        Assert.True(p.TryGetExistingRefund("key-abc", out var found));
        Assert.Equal("R1", found!.PayPalRefundId);
        Assert.False(p.TryGetExistingRefund("other", out _));
    }
}
