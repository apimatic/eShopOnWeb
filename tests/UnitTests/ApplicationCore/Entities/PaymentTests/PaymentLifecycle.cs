using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment Captured(decimal amount = 29m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@example.com", currency: "USD", amount: amount);
        payment.MarkAuthorized("ORDER1", "AUTH1", "CREATED", null, "VISA", "1111");
        payment.MarkCaptured("CAP1", "COMPLETED", capturedAmount: amount, payPalFee: 1.24m, netAmount: amount - 1.24m);
        return payment;
    }

    [Fact]
    public void NewPaymentAwaitsPayment()
    {
        var payment = new Payment(1, "buyer@example.com", "USD", 10m);
        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(0m, payment.RefundedAmount());
    }

    [Fact]
    public void AuthorizeThenCaptureTracksBreakdown()
    {
        var payment = Captured();
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(29m, payment.CapturedAmount);
        Assert.Equal(1.24m, payment.PayPalFee);
        Assert.Equal(27.76m, payment.NetAmount);
        Assert.Equal(29m, payment.RemainingRefundable());
    }

    [Fact]
    public void PartialRefundLeavesRemainderRefundable()
    {
        var payment = Captured();
        payment.AddRefund(new PaymentRefund("R1", 5m, "USD", "COMPLETED", "k1", null));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(5m, payment.RefundedAmount());
        Assert.Equal(24m, payment.RemainingRefundable());
    }

    [Fact]
    public void RefundingTheWholeCaptureMarksRefunded()
    {
        var payment = Captured();
        payment.AddRefund(new PaymentRefund("R1", 20m, "USD", "COMPLETED", "k1", null));
        payment.AddRefund(new PaymentRefund("R2", 9m, "USD", "COMPLETED", "k2", null));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable());
    }

    [Fact]
    public void FindRefundByKeyReturnsTheMatchingRefund()
    {
        var payment = Captured();
        payment.AddRefund(new PaymentRefund("R1", 5m, "USD", "COMPLETED", "dup-key", null));

        Assert.NotNull(payment.FindRefundByKey("dup-key"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }

    [Fact]
    public void AuthorizationAttemptsIncrementForFreshIdempotencyKeys()
    {
        var payment = new Payment(1, "buyer@example.com", "USD", 10m);
        Assert.Equal(1, payment.NextAuthorizationAttempt());
        Assert.Equal(2, payment.NextAuthorizationAttempt());
    }
}
