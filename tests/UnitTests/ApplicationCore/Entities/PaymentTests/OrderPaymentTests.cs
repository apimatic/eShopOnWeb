using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewPayment(decimal amount = 100m) =>
        new(orderId: 1, buyerId: "buyer@test.com", amount: amount, currencyCode: "USD", merchantReference: "ESHOP-1-abc");

    private static OrderPayment Authorized(decimal amount = 100m)
    {
        var payment = NewPayment(amount);
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.Now.AddDays(3), null);
        return payment;
    }

    private static OrderPayment Captured(decimal amount = 100m)
    {
        var payment = Authorized(amount);
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithIdempotencyKeys()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.PendingAuthorization, payment.Status);
        Assert.False(string.IsNullOrEmpty(payment.AuthorizationRequestId));
        Assert.False(string.IsNullOrEmpty(payment.CaptureRequestId));
        Assert.False(string.IsNullOrEmpty(payment.VoidRequestId));
    }

    [Fact]
    public void MarkAuthorized_TransitionsToAuthorized()
    {
        var payment = Authorized();

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal("PPO-1", payment.PayPalOrderId);
    }

    [Fact]
    public void MarkCaptured_RecordsFeeAndNet()
    {
        var payment = Captured(100m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(100m, payment.CapturedGross);
        Assert.Equal(3m, payment.PayPalFee);
        Assert.Equal(97m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableRemaining());
    }

    [Fact]
    public void Capture_BeforeAuthorization_Throws()
    {
        var payment = NewPayment();
        Assert.Throws<PaymentException>(() => payment.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m));
    }

    [Fact]
    public void Void_OnlyValidWhileAuthorized()
    {
        var captured = Captured();
        Assert.Throws<PaymentException>(() => captured.MarkVoided("VOIDED"));

        var authorized = Authorized();
        authorized.MarkVoided("VOIDED");
        Assert.Equal(PaymentStatus.Voided, authorized.Status);
    }

    [Fact]
    public void PartialRefund_SetsPartiallyRefunded_AndReducesRemaining()
    {
        var payment = Captured(100m);

        payment.AddRefund("REF-1", 30m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(30m, payment.TotalRefunded());
        Assert.Equal(70m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refunds_NeverExceedTheCapturedAmount()
    {
        var payment = Captured(100m);
        payment.AddRefund("REF-1", 60m, "COMPLETED", "key-1");

        Assert.Throws<PaymentException>(() => payment.AddRefund("REF-2", 50m, "COMPLETED", "key-2"));
    }

    [Fact]
    public void TwoPartialRefunds_ToFull_MarkRefunded()
    {
        var payment = Captured(100m);
        payment.AddRefund("REF-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("REF-2", 60m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExistingRefund()
    {
        var payment = Captured(100m);
        var refund = payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1");

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void FailedRefund_DoesNotConsumeRefundableBalance()
    {
        var payment = Captured(100m);
        payment.AddRefund("REF-1", 100m, "FAILED", "key-1");

        Assert.Equal(0m, payment.TotalRefunded());
        Assert.Equal(100m, payment.RefundableRemaining());
    }
}
