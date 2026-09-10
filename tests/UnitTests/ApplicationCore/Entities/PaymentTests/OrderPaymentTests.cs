using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment CapturedPayment(decimal amount = 50m)
    {
        var payment = new OrderPayment(1, "buyer@test", "USD", amount);
        payment.SetInvoiceReference("eshop-1-abc");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(28).ToString("O"));
        payment.MarkFulfilled("CAP1", "COMPLETED", amount, 2m, amount - 2m);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var payment = new OrderPayment(1, "buyer@test", "USD", 10m);
        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(0m, payment.RefundedAmount);
    }

    [Fact]
    public void PartialRefundLeavesOrderPartiallyRefundedWithReducedRemaining()
    {
        var payment = CapturedPayment(50m);

        payment.AddRefund(new PaymentRefund("k1", 20m, "R1", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(20m, payment.RefundedAmount);
        Assert.Equal(30m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundsSummingToCaptureMarkOrderRefunded()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund(new PaymentRefund("k1", 20m, "R1", "COMPLETED"));
        payment.AddRefund(new PaymentRefund("k2", 30m, "R2", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundBeyondRemainingIsRejected()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund(new PaymentRefund("k1", 40m, "R1", "COMPLETED"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => payment.AddRefund(new PaymentRefund("k2", 20m, "R2", "COMPLETED")));
        Assert.Contains("exceeds the refundable remaining", ex.Message);
        Assert.Equal(40m, payment.RefundedAmount); // unchanged
    }

    [Fact]
    public void FindRefundByKeyReturnsPriorRefund()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund(new PaymentRefund("dup-key", 5m, "R1", "COMPLETED"));

        Assert.NotNull(payment.FindRefundByKey("dup-key"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }
}
