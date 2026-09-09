using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentTests
{
    private const string BuyerId = "demouser@microsoft.com";

    private static OrderPayment NewPayment(decimal amount = 47.50m) =>
        new(orderId: 1, buyerId: BuyerId, currency: "USD", amount: amount);

    [Fact]
    public void NewPayment_IsPendingPayment_WithUniqueStableToken()
    {
        var a = NewPayment();
        var b = NewPayment();

        Assert.Equal(PaymentStatus.PendingPayment, a.Status);
        Assert.False(string.IsNullOrWhiteSpace(a.IdempotencyToken));
        Assert.NotEqual(a.IdempotencyToken, b.IdempotencyToken); // unique across orders / restarts
    }

    [Fact]
    public void RecordAuthorization_MovesToAuthorized_AndStoresPayPalState()
    {
        var payment = NewPayment();

        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.True(payment.IsAuthorized);
        Assert.Equal("PPORDER1", payment.PayPalOrderId);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal("CREATED", payment.AuthorizationStatus);
    }

    [Fact]
    public void RecordCapture_MovesToFulfilled_AndStoresBreakdown()
    {
        var payment = NewPayment();
        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");

        payment.RecordCapture("CAP1", "AUTH1", "COMPLETED", gross: 47.50m, fee: 1.72m, net: 45.78m);

        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
        Assert.True(payment.IsFulfilled);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(47.50m, payment.CapturedGross);
        Assert.Equal(1.72m, payment.PayPalFee);
        Assert.Equal(45.78m, payment.NetAmount);
        Assert.Equal(47.50m, payment.RefundableRemaining);
    }

    [Fact]
    public void RecordCapture_WithRenewedAuthorization_UpdatesEffectiveAuthId()
    {
        var payment = NewPayment();
        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");

        payment.RecordCapture("CAP1", "AUTH2", "COMPLETED", 47.50m, 1.72m, 45.78m);

        Assert.Equal("AUTH2", payment.AuthorizationId); // reauthorized id replaces the stale one
    }

    [Fact]
    public void RecordRefund_Partial_ThenRemainder_CapsAtCapturedAndTransitionsStatus()
    {
        var payment = NewPayment();
        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");
        payment.RecordCapture("CAP1", "AUTH1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        payment.RecordRefund("REF1", 20.00m, "key-1", "COMPLETED");
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(20.00m, payment.RefundedAmount);
        Assert.Equal(27.50m, payment.RefundableRemaining);

        payment.RecordRefund("REF2", 27.50m, "key-2", "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(47.50m, payment.RefundedAmount);
        Assert.Equal(0m, payment.RefundableRemaining); // never refundable beyond what was captured
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsMatchingRefund()
    {
        var payment = NewPayment();
        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");
        payment.RecordCapture("CAP1", "AUTH1", "COMPLETED", 47.50m, 1.72m, 45.78m);
        payment.RecordRefund("REF1", 10m, "idem-abc", "COMPLETED");

        var found = payment.FindRefundByIdempotencyKey("idem-abc");
        Assert.NotNull(found);
        Assert.Equal("REF1", found!.RefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("does-not-exist"));
    }

    [Fact]
    public void RecordCancellation_MovesToCancelled()
    {
        var payment = NewPayment();
        payment.RecordAuthorization("PPORDER1", "AUTH1", "CREATED");

        payment.RecordCancellation();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void MarkFailed_RecordsReason()
    {
        var payment = NewPayment();

        payment.MarkFailed("card declined");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card declined", payment.FailureReason);
    }
}
