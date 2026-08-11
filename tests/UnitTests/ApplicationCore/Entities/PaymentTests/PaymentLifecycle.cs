using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 100m) =>
        new Payment(orderId: 1, buyerId: "buyer-1", amount: amount, currency: "USD");

    private static Payment Captured(decimal amount = 100m)
    {
        var payment = NewPayment(amount);
        payment.SetAuthorization("PPORDER", "AUTH1", "CREATED");
        payment.SetCapture("CAP1", "COMPLETED", amount, paypalFee: 3m, netAmount: amount - 3m);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var payment = NewPayment();
        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void AuthorizationThenCaptureMovesThroughStates()
    {
        var payment = NewPayment();
        payment.SetAuthorization("PPORDER", "AUTH1", "CREATED");
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);

        payment.SetCapture("CAP1", "COMPLETED", 100m, 3m, 97m);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(97m, payment.NetAmount);
    }

    [Fact]
    public void PartialRefundLeavesPartiallyRefundedAndTracksRemaining()
    {
        var payment = Captured(100m);
        payment.AddRefund("R1", 30m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(30m, payment.TotalRefunded);
        Assert.Equal(70m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundsSummingToCaptureBecomeFullyRefunded()
    {
        var payment = Captured(100m);
        payment.AddRefund("R1", 60m, "COMPLETED", "key-1");
        payment.AddRefund("R2", 40m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FailedRefundDoesNotCountAgainstCapture()
    {
        var payment = Captured(100m);
        payment.AddRefund("R1", 50m, "FAILED", "key-1");

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(100m, payment.RefundableRemaining);
    }

    [Fact]
    public void FindsRecordedRefundByIdempotencyKey()
    {
        var payment = Captured(100m);
        payment.AddRefund("R1", 25m, "COMPLETED", "key-1");

        Assert.NotNull(payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void IdempotencyKeysAreStablePerPaymentAndUniquePerInstance()
    {
        var a = NewPayment();
        var b = NewPayment();

        // Stable across reads for the same payment/attempt (survives a double-click).
        Assert.Equal(a.AuthorizeIdempotencyKey, a.AuthorizeIdempotencyKey);
        Assert.Equal(a.CaptureIdempotencyKey, a.CaptureIdempotencyKey);

        // Unique per payment instance (so a reset order id cannot collide across runs).
        Assert.NotEqual(a.AuthorizeIdempotencyKey, b.AuthorizeIdempotencyKey);
        Assert.NotEqual(a.CaptureIdempotencyKey, b.CaptureIdempotencyKey);
    }

    [Fact]
    public void RecordingAuthorizeFailureRotatesTheAuthorizeKey()
    {
        var payment = NewPayment();
        var firstKey = payment.AuthorizeIdempotencyKey;

        payment.RecordAuthorizeFailure();

        Assert.NotEqual(firstKey, payment.AuthorizeIdempotencyKey);
    }

    [Fact]
    public void CancelMarksCancelledAndVoidsHold()
    {
        var payment = NewPayment();
        payment.SetAuthorization("PPORDER", "AUTH1", "CREATED");

        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
