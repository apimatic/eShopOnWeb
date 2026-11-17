using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    [Fact]
    public void AuthorizeCaptureAndPartialRefundsTrackTheFinixOwnedState()
    {
        var payment = new OrderPayment(42, "buyer@example.com", 1950, "USD");
        var attemptKey = payment.PaymentAttemptKey;

        payment.MarkAuthorized("AUTH-1", "SUCCEEDED", DateTimeOffset.UtcNow.AddDays(7), "card-1", null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal("card-1", payment.PaymentMethodId);

        payment.MarkCaptured("TRANSFER-1", "SUCCEEDED", 1950, 50);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(1950, payment.CapturedCents);
        Assert.Equal(50, payment.FeeCents);
        Assert.Equal(1950, payment.RefundableCents);

        payment.RecordRefund(500);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(500, payment.AmountRefundedCents);
        Assert.Equal(1450, payment.RefundableCents);

        payment.RecordRefund(1450);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(1950, payment.AmountRefundedCents);
        Assert.Equal(0, payment.RefundableCents);
        Assert.Equal(attemptKey, payment.PaymentAttemptKey);
    }

    [Fact]
    public void CancelMarksThePaymentCanceledWithoutCapturing()
    {
        var payment = new OrderPayment(7, "buyer@example.com", 850, "USD");
        payment.MarkAuthorized("AUTH-2", "SUCCEEDED", null, null, null);

        payment.MarkCanceled("SUCCEEDED");

        Assert.Equal(PaymentStatus.Canceled, payment.Status);
        Assert.Equal(0, payment.CapturedCents);
        Assert.Equal("SUCCEEDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void UnknownOutcomeKeepsTheIdempotencyKeyButDefinitiveFailureRotatesIt()
    {
        var payment = new OrderPayment(9, "buyer@example.com", 1200, "USD");
        var attemptKey = payment.PaymentAttemptKey;

        payment.MarkPaymentUnknown("UNKNOWN", "transport failed");

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(attemptKey, payment.PaymentAttemptKey);

        payment.MarkPaymentFailed("DECLINED", "card declined");

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.NotEqual(attemptKey, payment.PaymentAttemptKey);
        Assert.Equal("DECLINED", payment.LastFailureCode);
    }
}
