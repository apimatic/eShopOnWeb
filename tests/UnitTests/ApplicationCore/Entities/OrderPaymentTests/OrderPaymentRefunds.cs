using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentRefunds
{
    private static OrderPayment CapturedPayment(decimal amount = 100m)
    {
        var payment = new OrderPayment(orderId: 1, buyerId: "buyer-1", currencyCode: "USD", amount: amount);
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, fee: 3m, net: amount - 3m);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPaymentWithAUniqueReference()
    {
        var payment = new OrderPayment(1, "buyer-1", "USD", 10m);

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.False(string.IsNullOrWhiteSpace(payment.Reference));
        Assert.NotEqual(new OrderPayment(1, "buyer-1", "USD", 10m).Reference, payment.Reference);
    }

    [Fact]
    public void TwoPartialRefundsAreAllowedAndTrackRemaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund(new PaymentRefund("REF-1", "k1", 30m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(70m, payment.RemainingRefundable());

        payment.AddRefund(new PaymentRefund("REF-2", "k2", 20m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(50m, payment.RemainingRefundable());
        Assert.Equal(50m, payment.RefundedAmount());
    }

    [Fact]
    public void FullyRefundedWhenRemainingReachesZero()
    {
        var payment = CapturedPayment(40m);

        payment.AddRefund(new PaymentRefund("REF-1", "k1", 40m, "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable());
    }

    [Fact]
    public void RefundBeyondCaptureIsRejected()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund(new PaymentRefund("REF-1", "k1", 40m, "COMPLETED"));

        // Only 10 remains; a 20 refund must never take the total beyond what was captured.
        var ex = Assert.Throws<InvalidOperationException>(
            () => payment.AddRefund(new PaymentRefund("REF-2", "k2", 20m, "COMPLETED")));
        Assert.Contains("exceeds", ex.Message);
        Assert.Equal(10m, payment.RemainingRefundable());
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new OrderPayment(1, "buyer-1", "USD", 100m);
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", null);

        Assert.Throws<InvalidOperationException>(
            () => payment.AddRefund(new PaymentRefund("REF-1", "k1", 10m, "COMPLETED")));
    }

    [Fact]
    public void FindRefundByKeyReturnsTheRecordedRefund()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new PaymentRefund("REF-1", "key-abc", 25m, "COMPLETED"));

        var found = payment.FindRefundByKey("key-abc");
        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByKey("other-key"));
    }

    [Fact]
    public void CaptureRecordsFeeAndNetProceeds()
    {
        var payment = CapturedPayment(100m);

        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3m, payment.PayPalFee);
        Assert.Equal(97m, payment.NetAmount);
        Assert.NotNull(payment.CapturedAt);
    }
}
