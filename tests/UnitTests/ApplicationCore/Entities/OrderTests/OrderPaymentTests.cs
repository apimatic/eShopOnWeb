using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    [Fact]
    public void TracksCaptureFeesNetAndPartialRefunds()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = new OrderPayment("USD", 100m);
        payment.SetPayPalOrder("ORDER-ID");
        payment.RecordAuthorization("AUTH-ID", "CREATED", now, now.AddDays(29));
        payment.RecordCapture("CAPTURE-ID", "COMPLETED", 100m, 3m, 97m, now);

        payment.AddRefund("partial-one", "REFUND-1", "COMPLETED", 30m, now);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(30m, payment.RefundedAmount);
        Assert.Equal(70m, payment.RefundableAmount);
        Assert.Equal(3m, payment.PayPalFee);
        Assert.Equal(97m, payment.NetProceeds);

        payment.AddRefund("partial-two", "REFUND-2", "COMPLETED", 70m, now);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void ReplayingARefundKeyReturnsTheOriginalRefund()
    {
        var payment = CapturedPayment();
        var first = payment.AddRefund("same-key", "REFUND-1", "COMPLETED", 25m, DateTimeOffset.UtcNow);
        var replay = payment.AddRefund("same-key", "REFUND-OTHER", "COMPLETED", 99m, DateTimeOffset.UtcNow);

        Assert.Same(first, replay);
        Assert.Single(payment.Refunds);
        Assert.Equal(25m, payment.RefundedAmount);
    }

    [Fact]
    public void CannotRefundBeyondTheCapturedBalance()
    {
        var payment = CapturedPayment();
        payment.AddRefund("first", "REFUND-1", "COMPLETED", 80m, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund("second", "REFUND-2", "COMPLETED", 21m, DateTimeOffset.UtcNow));
    }

    private static OrderPayment CapturedPayment()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = new OrderPayment("USD", 100m);
        payment.SetPayPalOrder("ORDER-ID");
        payment.RecordAuthorization("AUTH-ID", "CREATED", now, now.AddDays(29));
        payment.RecordCapture("CAPTURE-ID", "COMPLETED", 100m, 3m, 97m, now);
        return payment;
    }
}
