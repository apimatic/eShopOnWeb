using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundRules
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(1, "buyer@example.com", amount, "USD", "eshop-1-20260828120000");
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);
        payment.RecordCapture("PP-CAPTURE", "COMPLETED", amount, 3.20m, amount - 3.20m);
        return payment;
    }

    [Fact]
    public void FullRefundOfAnUntouchedCaptureUsesTheWholeCapturedAmount()
    {
        var payment = CapturedPayment();

        Assert.Equal(100m, payment.ValidateRefundAmount(null));
    }

    [Fact]
    public void PartiallyRefundedPaymentIsNeverRefundableBeyondWhatWasCaptured()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-1", "PP-REFUND-1", "COMPLETED", 60m);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.RefundableRemaining);

        // The whole point: the remaining balance caps the next refund, not the original capture.
        var exceeded = Assert.Throws<PaymentValidationException>(() => payment.ValidateRefundAmount(41m));
        Assert.Contains("40.00", exceeded.Message);

        Assert.Equal(40m, payment.ValidateRefundAmount(null));
    }

    [Fact]
    public void RefundingTheRemainderMarksThePaymentFullyRefunded()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-1", "PP-REFUND-1", "COMPLETED", 60m);
        payment.AddRefund("key-2", "PP-REFUND-2", "COMPLETED", 40m);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);

        // Fully refunded is a state conflict, not a bad request — there is nothing left to refund.
        Assert.Throws<OrderStateException>(() => payment.ValidateRefundAmount(null));
    }

    [Fact]
    public void ARefundTheProcessorFailedDoesNotConsumeTheRefundableBalance()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-1", "PP-REFUND-1", "FAILED", 60m);

        // No money was returned, so all of it is still refundable and the payment is still captured.
        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(100m, payment.RefundableRemaining);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void ARepeatedIdempotencyKeyFindsTheRefundItAlreadyProduced()
    {
        var payment = CapturedPayment();
        var first = payment.AddRefund("caller-key", "PP-REFUND-1", "COMPLETED", 10m);

        Assert.Same(first, payment.FindRefundByIdempotencyKey("caller-key"));
        Assert.Null(payment.FindRefundByIdempotencyKey("a-different-key"));
    }

    [Fact]
    public void ZeroOrNegativeRefundAmountsAreRejected()
    {
        var payment = CapturedPayment();

        Assert.Throws<PaymentValidationException>(() => payment.ValidateRefundAmount(0m));
        Assert.Throws<PaymentValidationException>(() => payment.ValidateRefundAmount(-5m));
    }

    [Fact]
    public void AnUncapturedPaymentCannotBeRefunded()
    {
        var payment = new Payment(1, "buyer@example.com", 100m, "USD", "eshop-1-20260828120000");
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);

        Assert.Throws<OrderStateException>(() => payment.ValidateRefundAmount(10m));
    }
}
