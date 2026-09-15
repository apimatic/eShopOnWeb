using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewPayment(decimal amount = 100m) =>
        new(orderId: 1, buyerId: "buyer@example.com", currencyCode: "USD", amount: amount,
            invoiceId: "ESHOP-1-abc", requestId: "req-1");

    private static OrderPayment CapturedPayment(decimal amount = 100m)
    {
        var p = NewPayment(amount);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        p.MarkCaptured("CAP1", "COMPLETED", amount, payPalFee: 3m, netAmount: amount - 3m);
        return p;
    }

    [Fact]
    public void NewPayment_IsAwaitingPayment_WithNothingRefundable()
    {
        var p = NewPayment();
        Assert.Equal(PaymentStatus.AwaitingPayment, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
        Assert.False(p.IsFulfilled);
    }

    [Fact]
    public void MarkAuthorized_ThenCaptured_SetsStateAndFinancials()
    {
        var p = CapturedPayment(100m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("CAP1", p.CaptureId);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3m, p.PayPalFee);
        Assert.Equal(97m, p.NetAmount);
        Assert.Equal(100m, p.RefundableRemaining());
        Assert.True(p.IsFulfilled);
    }

    [Fact]
    public void PartialRefunds_ReduceRefundableRemaining_AndSetPartiallyRefunded()
    {
        var p = CapturedPayment(100m);

        var r1 = new PaymentRefund("key-A", 30m);
        p.AddRefund(r1);
        r1.SetResult("RF1", "COMPLETED", 30m);
        p.RecalculateRefundStatus();

        Assert.Equal(30m, p.TotalRefunded());
        Assert.Equal(70m, p.RefundableRemaining());
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
    }

    [Fact]
    public void RefundingFullCapturedAmount_SetsRefunded()
    {
        var p = CapturedPayment(100m);
        var r = new PaymentRefund("key-full", 100m);
        p.AddRefund(r);
        r.SetResult("RF", "COMPLETED", 100m);
        p.RecalculateRefundStatus();

        Assert.Equal(0m, p.RefundableRemaining());
        Assert.Equal(PaymentStatus.Refunded, p.Status);
    }

    [Fact]
    public void AddRefund_BeyondRemaining_Throws()
    {
        var p = CapturedPayment(100m);
        p.AddRefund(new PaymentRefund("key-A", 60m));

        // Only 40 remains; a 50 refund must be rejected so the order can never be over-refunded.
        Assert.Throws<InvalidOperationException>(() => p.AddRefund(new PaymentRefund("key-B", 50m)));
    }

    [Fact]
    public void AddRefund_WhenNotCaptured_Throws()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        Assert.Throws<InvalidOperationException>(() => p.AddRefund(new PaymentRefund("key", 10m)));
    }

    [Fact]
    public void FailedRefund_DoesNotCountAgainstCapture()
    {
        var p = CapturedPayment(100m);
        var r = new PaymentRefund("key-A", 40m);
        p.AddRefund(r);
        r.MarkFailed();
        p.RecalculateRefundStatus();

        Assert.Equal(0m, p.TotalRefunded());
        Assert.Equal(100m, p.RefundableRemaining());
        Assert.Equal(PaymentStatus.Captured, p.Status);
    }

    [Fact]
    public void Cancel_MovesToCancelled()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        p.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
    }
}
