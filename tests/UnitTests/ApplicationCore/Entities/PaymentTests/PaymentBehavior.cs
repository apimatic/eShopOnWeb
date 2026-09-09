using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment NewAuthorizedPayment(decimal amount = 29m) =>
        new(orderId: 1, buyerId: "buyer@example.com", currency: "USD", authorizedAmount: amount,
            payPalOrderId: "PPO-1", authorizationId: "AUTH-1", authorizationStatus: "CREATED",
            authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(3), createOrderRequestId: "auth-1");

    private static Payment NewCapturedPayment(decimal amount = 29m)
    {
        var payment = NewAuthorizedPayment(amount);
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, payPalFee: 1.24m, netAmount: amount - 1.24m,
            captureRequestId: "capture-1");
        return payment;
    }

    [Fact]
    public void NewPaymentIsAuthorizedWithNoRefundableBalance()
    {
        var payment = NewAuthorizedPayment();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable);
    }

    [Fact]
    public void CaptureRecordsFeeAndNetAndOpensRefundableBalance()
    {
        var payment = NewCapturedPayment(29m);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(29m, payment.CapturedGrossAmount);
        Assert.Equal(1.24m, payment.PayPalFee);
        Assert.Equal(27.76m, payment.NetAmount);
        Assert.Equal(29m, payment.RemainingRefundable);
    }

    [Fact]
    public void CannotCaptureTwice()
    {
        var payment = NewCapturedPayment();
        Assert.Throws<InvalidPaymentStateException>(() =>
            payment.MarkCaptured("CAP-2", "COMPLETED", 29m, 1m, 28m, "capture-2"));
    }

    [Fact]
    public void CannotVoidAfterCapture()
    {
        var payment = NewCapturedPayment();
        Assert.Throws<InvalidPaymentStateException>(() => payment.MarkVoided());
    }

    [Fact]
    public void PartialRefundLeavesRemainderRefundable()
    {
        var payment = NewCapturedPayment(29m);
        payment.AddRefund("REF-1", 9m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(9m, payment.TotalRefunded);
        Assert.Equal(20m, payment.RemainingRefundable);
    }

    [Fact]
    public void RefundsAddingUpToCaptureMarkPaymentRefunded()
    {
        var payment = NewCapturedPayment(29m);
        payment.AddRefund("REF-1", 9m, "COMPLETED", "key-1");
        payment.AddRefund("REF-2", 20m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable);
    }

    [Fact]
    public void GuardRefundableRejectsAmountBeyondRemaining()
    {
        var payment = NewCapturedPayment(29m);
        payment.AddRefund("REF-1", 25m, "COMPLETED", "key-1");

        // Only 4.00 remains; a 10.00 refund must be rejected before contacting the gateway.
        Assert.Throws<RefundExceedsCaptureException>(() => payment.GuardRefundable(10m));
    }

    [Fact]
    public void GuardRefundableRejectsRefundOfUncapturedPayment()
    {
        var payment = NewAuthorizedPayment();
        Assert.Throws<InvalidPaymentStateException>(() => payment.GuardRefundable(1m));
    }

    [Fact]
    public void FailedRefundDoesNotReserveFunds()
    {
        var payment = NewCapturedPayment(29m);
        payment.AddRefund("REF-1", 29m, "FAILED", "key-1");

        // A FAILED refund must not consume the refundable balance.
        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(29m, payment.RemainingRefundable);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsPriorRefund()
    {
        var payment = NewCapturedPayment(29m);
        var first = payment.AddRefund("REF-1", 9m, "COMPLETED", "key-1");

        Assert.Same(first, payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }
}
