using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

/// <summary>Locks in the payment-state invariants the endpoints rely on.</summary>
public class OrderPaymentTests
{
    private static OrderPayment CapturedPayment(decimal amount = 100m)
    {
        var payment = new OrderPayment(1, "buyer@example.com", "USD", amount, "eshop-1-ref");
        payment.MarkAuthorized("ORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        payment.MarkCaptured("CAP1", "COMPLETED", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void A_partial_refund_moves_the_status_to_PartiallyRefunded_and_reduces_the_remaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund(new PaymentRefund("k1", 40m, "R1", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded());
        Assert.Equal(60m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refunding_the_whole_remaining_moves_the_status_to_Refunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new PaymentRefund("k1", 40m, "R1", "COMPLETED"));

        payment.AddRefund(new PaymentRefund("k2", 60m, "R2", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void A_refund_beyond_the_captured_amount_is_rejected()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new PaymentRefund("k1", 80m, "R1", "COMPLETED"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => payment.AddRefund(new PaymentRefund("k2", 30m, "R2", "COMPLETED")));

        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(20m, payment.RefundableRemaining());
    }

    [Fact]
    public void An_order_cannot_be_cancelled_after_it_has_been_captured()
    {
        var payment = CapturedPayment();

        Assert.Throws<InvalidOperationException>(() => payment.MarkCanceled());
    }

    [Fact]
    public void An_authorized_order_can_be_cancelled_before_capture()
    {
        var payment = new OrderPayment(1, "buyer@example.com", "USD", 50m, "eshop-1-ref");
        payment.MarkAuthorized("ORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);

        payment.MarkCanceled();

        Assert.Equal(PaymentStatus.Canceled, payment.Status);
    }
}
