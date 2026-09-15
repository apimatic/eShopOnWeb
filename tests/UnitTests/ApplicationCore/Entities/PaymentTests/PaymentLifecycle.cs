using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 36.50m) =>
        new(orderId: 1, buyerId: "demouser@microsoft.com", amount: amount, currencyCode: "USD");

    private static Payment Authorized(decimal amount = 36.50m)
    {
        var payment = NewPayment(amount);
        payment.PrepareInvoice("ESHOP-1-abc");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");
        return payment;
    }

    [Fact]
    public void NewPaymentIsAwaitingPaymentWithUniqueToken()
    {
        var a = NewPayment();
        var b = NewPayment();

        Assert.Equal(PaymentStatus.AwaitingPayment, a.Status);
        Assert.False(string.IsNullOrEmpty(a.IdempotencyToken));
        Assert.NotEqual(a.IdempotencyToken, b.IdempotencyToken);
    }

    [Fact]
    public void FullLifecycleAuthorizeCaptureRefund()
    {
        var payment = Authorized();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);

        payment.MarkCaptured("CAP1", "COMPLETED", 36.50m, 1.44m, 35.06m);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(36.50m, payment.CapturedAmount);
        Assert.Equal(36.50m, payment.RefundableRemaining());

        payment.AddRefund("k1", "REF1", 10m, "COMPLETED");
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(26.50m, payment.RefundableRemaining());

        payment.AddRefund("k2", "REF2", 26.50m, "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var payment = Authorized();
        payment.MarkCaptured("CAP1", "COMPLETED", 36.50m, null, null);

        var ex = Assert.Throws<PaymentException>(() => payment.EnsureRefundable(40m));
        Assert.Contains("exceeds", ex.Message);

        // A partial refund then an over-refund of the remainder is also rejected.
        payment.AddRefund("k1", "REF1", 30m, "COMPLETED");
        Assert.Throws<PaymentException>(() => payment.EnsureRefundable(10m));
    }

    [Fact]
    public void CannotCaptureWithoutAuthorization()
    {
        var payment = NewPayment();
        Assert.Throws<PaymentException>(() => payment.MarkCaptured("CAP1", "COMPLETED", 36.50m, null, null));
    }

    [Fact]
    public void CannotVoidAfterCapture()
    {
        var payment = Authorized();
        payment.MarkCaptured("CAP1", "COMPLETED", 36.50m, null, null);
        Assert.Throws<PaymentException>(() => payment.MarkVoided());
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = Authorized();
        Assert.Throws<PaymentException>(() => payment.EnsureRefundable(1m));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsRecordedRefund()
    {
        var payment = Authorized();
        payment.MarkCaptured("CAP1", "COMPLETED", 36.50m, null, null);
        payment.AddRefund("dup-key", "REF1", 5m, "COMPLETED");

        var found = payment.FindRefundByIdempotencyKey("dup-key");
        Assert.NotNull(found);
        Assert.Equal("REF1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void MarkVoidedReleasesFundsBeforeFulfilment()
    {
        var payment = Authorized();
        payment.MarkVoided();
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
