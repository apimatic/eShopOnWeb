using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentRefundTests
{
    private static OrderPayment CapturedPayment(decimal captured = 51m)
    {
        var payment = new OrderPayment(orderId: 1, buyerId: "buyer@test", amount: captured, currency: "USD");
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", "VISA ****1111");
        payment.SetCaptured("CAP-1", "COMPLETED", captured, payPalFee: 1.81m, netAmount: captured - 1.81m);
        return payment;
    }

    [Fact]
    public void PartialRefundLeavesPaymentPartiallyRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("R1", 10m, "key-1", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.TotalRefunded());
        Assert.Equal(41m, payment.RefundableRemaining());
    }

    [Fact]
    public void RefundingTheRemainderMarksItRefunded()
    {
        var payment = CapturedPayment();
        payment.AddRefund("R1", 10m, "key-1", "COMPLETED");

        payment.AddRefund("R2", 41m, "key-2", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var payment = CapturedPayment();

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("R1", 60m, "key-1", "COMPLETED"));
    }

    [Fact]
    public void CannotRefundBeyondRemainingAcrossMultipleRefunds()
    {
        var payment = CapturedPayment();
        payment.AddRefund("R1", 50m, "key-1", "COMPLETED");

        // Only 1.00 remains; a 2.00 refund must be rejected.
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("R2", 2m, "key-2", "COMPLETED"));
    }

    [Fact]
    public void FindRefundByKeyReturnsTheMatchingRefund()
    {
        var payment = CapturedPayment();
        payment.AddRefund("R1", 10m, "key-1", "COMPLETED");

        Assert.NotNull(payment.FindRefundByKey("key-1"));
        Assert.Null(payment.FindRefundByKey("missing"));
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new OrderPayment(1, "buyer@test", 51m, "USD");
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", null);

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("R1", 1m, "key-1", "COMPLETED"));
    }
}
