using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundRules
{
    private static Payment AuthorizedPayment(decimal amount = 100m) =>
        new(orderId: 1, buyerId: "buyer@example.com", currencyCode: "USD", amount: amount,
            payPalOrderId: "PPORDER1", authorizationId: "AUTH1",
            authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(3));

    private static Payment CapturedPayment(decimal captured = 100m)
    {
        var payment = AuthorizedPayment(captured);
        payment.MarkCaptured("CAP1", captured, payPalFee: 3.5m, netAmount: captured - 3.5m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsAuthorized()
    {
        var payment = AuthorizedPayment();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
    }

    [Fact]
    public void CaptureRecordsPayPalFigures()
    {
        var payment = CapturedPayment(100m);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3.5m, payment.PayPalFee);
        Assert.Equal(96.5m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableRemaining);
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = AuthorizedPayment();
        Assert.Throws<InvalidOperationException>(() => payment.GuardCanRefund(10m));
    }

    [Fact]
    public void RefundCannotExceedCapturedAmount()
    {
        var payment = CapturedPayment(100m);
        Assert.Throws<InvalidOperationException>(() => payment.GuardCanRefund(100.01m));
    }

    [Fact]
    public void PartialRefundLeavesOrderPartiallyRefundedAndNeverOverRefundable()
    {
        var payment = CapturedPayment(100m);

        payment.GuardCanRefund(30m);
        payment.AddRefund("REF1", "key-1", 30m, "COMPLETED");
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(30m, payment.TotalRefunded);
        Assert.Equal(70m, payment.RefundableRemaining);

        // A second, distinct partial refund is legitimate.
        payment.GuardCanRefund(70m);
        payment.AddRefund("REF2", "key-2", 70m, "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(100m, payment.TotalRefunded);
        Assert.Equal(0m, payment.RefundableRemaining);

        // Now nothing more is refundable.
        Assert.Throws<InvalidOperationException>(() => payment.GuardCanRefund(0.01m));
    }

    [Fact]
    public void RefundIdempotencyKeyIsFindable()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("REF1", "key-1", 40m, "COMPLETED");

        var found = payment.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(found);
        Assert.Equal("REF1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CannotCancelAfterCapture()
    {
        var payment = CapturedPayment(100m);
        Assert.Throws<InvalidOperationException>(() => payment.MarkVoided());
    }

    [Fact]
    public void VoidedAuthorizationCannotBeCaptured()
    {
        var payment = AuthorizedPayment();
        payment.MarkVoided();
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Throws<InvalidOperationException>(() =>
            payment.MarkCaptured("CAP1", 100m, 3m, 97m));
    }

    [Fact]
    public void StaleAuthorizationCanBeRenewed()
    {
        var payment = AuthorizedPayment();
        payment.RenewAuthorization("AUTH2", DateTimeOffset.UtcNow.AddDays(3));
        Assert.Equal("AUTH2", payment.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }
}
