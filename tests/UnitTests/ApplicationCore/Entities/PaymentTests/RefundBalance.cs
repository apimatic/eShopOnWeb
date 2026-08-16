using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class RefundBalance
{
    private static Payment CapturedPayment(decimal captured)
    {
        var payment = new Payment(
            orderId: 1, currency: "USD", payPalCustomId: "ESHOP-1-x", authorizedAmount: captured,
            payPalOrderId: "PPO", authorizationId: "AUTH", authorizationStatus: Payment.AuthCreated,
            authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(29), authorizationRequestId: "req",
            cardBrand: "VISA", cardLast4: "1111", savedCardId: null);
        payment.MarkCaptured("CAP", "COMPLETED", captured, payPalFee: 1m, netAmount: captured - 1m);
        return payment;
    }

    [Fact]
    public void FullBalanceIsRefundableBeforeAnyRefund()
    {
        var payment = CapturedPayment(29m);
        Assert.Equal(29m, payment.RefundableRemaining);
        Assert.Equal(0m, payment.TotalRefunded);
    }

    [Fact]
    public void EffectiveRefundsReduceTheRefundableBalance()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new Refund("k1", "R1", 10m, "USD", Refund.StatusCompleted));
        payment.AddRefund(new Refund("k2", "R2", 5m, "USD", Refund.StatusPending));

        Assert.Equal(15m, payment.TotalRefunded);
        Assert.Equal(14m, payment.RefundableRemaining);
    }

    [Fact]
    public void CancelledOrFailedRefundsDoNotConsumeBalance()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new Refund("k1", "R1", 10m, "USD", Refund.StatusCancelled));
        payment.AddRefund(new Refund("k2", "R2", 10m, "USD", Refund.StatusFailed));

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(29m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundableRemainingNeverGoesNegative()
    {
        var payment = CapturedPayment(10m);
        payment.AddRefund(new Refund("k1", "R1", 10m, "USD", Refund.StatusCompleted));
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FindRefundByKeyReturnsTheMatchingRefund()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new Refund("the-key", "R1", 10m, "USD", Refund.StatusCompleted));

        Assert.NotNull(payment.FindRefundByKey("the-key"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }
}
