using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

/// <summary>
/// Covers the refund invariants on the payment: a capture can be partly refunded repeatedly, but the
/// running total can never exceed what was captured, and idempotency keys are honoured.
/// </summary>
public class OrderPaymentRefunds
{
    private static OrderPayment CapturedPayment(decimal amount = 100m)
    {
        var payment = new OrderPayment("PayPal", "USD", amount, "ESHOP-1-ref", "VISA ending 1111");
        payment.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.RecordCapture("CAP-1", "COMPLETED", amount, 3.10m, amount - 3.10m);
        return payment;
    }

    [Fact]
    public void CaptureSetsCapturedStatusAndBreakdown()
    {
        var payment = CapturedPayment();

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3.10m, payment.PayPalFee);
        Assert.Equal(96.90m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableRemaining);
    }

    [Fact]
    public void PartialRefundReducesRemainingAndMarksPartiallyRefunded()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF-1", 25m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(25m, payment.TotalRefunded);
        Assert.Equal(75m, payment.RefundableRemaining);
    }

    [Fact]
    public void TwoDistinctPartialRefundsAccumulate()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF-1", 25m, "COMPLETED", "key-1");
        payment.AddRefund("REF-2", 30m, "COMPLETED", "key-2");

        Assert.Equal(55m, payment.TotalRefunded);
        Assert.Equal(45m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundingBeyondCapturedIsRejected()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF-1", 80m, "COMPLETED", "key-1");

        Assert.Throws<OrderPaymentException>(() => payment.AddRefund("REF-2", 40m, "COMPLETED", "key-2"));
    }

    [Fact]
    public void FullRefundMarksRefunded()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundIsLookedUpByIdempotencyKey()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1");

        Assert.NotNull(payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("key-unknown"));
    }

    [Fact]
    public void RefundBeforeCaptureIsRejected()
    {
        var payment = new OrderPayment("PayPal", "USD", 50m, "ESHOP-2-ref", "VISA ending 1111");
        payment.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Throws<OrderPaymentException>(() => payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1"));
    }
}
