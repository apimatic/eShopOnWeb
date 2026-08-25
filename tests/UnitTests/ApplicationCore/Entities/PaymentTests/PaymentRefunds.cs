using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefunds
{
    private static Payment CapturedPayment(decimal capturedAmount = 100m)
    {
        var payment = new Payment(1, capturedAmount, "USD");
        payment.RecordAuthorization("paypal-order-1", "auth-1", "CREATED", null, null);
        payment.RecordCapture("capture-1", "COMPLETED", capturedAmount, 3m, capturedAmount - 3m, DateTimeOffset.UtcNow);
        return payment;
    }

    [Fact]
    public void PartialRefundLeavesPaymentPartiallyRefunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 40m, RefundStatus.Completed, "key-1", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.RefundedAmount);
    }

    [Fact]
    public void RefundingTheFullCapturedAmountMarksPaymentRefunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 100m, RefundStatus.Completed, "key-1", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void TwoDistinctPartialRefundsCanSumToTheFullAmount()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 40m, RefundStatus.Completed, "key-1", DateTimeOffset.UtcNow);
        payment.AddRefund("refund-2", 60m, RefundStatus.Completed, "key-2", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(100m, payment.RefundedAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void RefundBeyondTheCapturedAmountThrows()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("refund-1", 60m, RefundStatus.Completed, "key-1", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund("refund-2", 60m, RefundStatus.Completed, "key-2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CannotRefundAPaymentThatWasNeverCaptured()
    {
        var payment = new Payment(1, 100m, "USD");
        payment.RecordAuthorization("paypal-order-1", "auth-1", "CREATED", null, null);

        Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund("refund-1", 10m, RefundStatus.Completed, "key-1", DateTimeOffset.UtcNow));
    }
}
