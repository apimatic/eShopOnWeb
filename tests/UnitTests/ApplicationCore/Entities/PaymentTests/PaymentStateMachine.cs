using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentStateMachine
{
    private static Payment NewAuthorizedPayment(decimal amount = 47.50m)
    {
        return new Payment(orderId: 1, buyerId: "demouser@microsoft.com", amount: amount, currency: "USD",
            payPalOrderId: "PAYPAL-ORDER-1", authorizationId: "AUTH-1", authorizationStatus: "CREATED",
            authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(3), savedCardId: null, cardBrand: "VISA", cardLast4: "1111");
    }

    [Fact]
    public void StartsInAuthorizedStatus()
    {
        var payment = NewAuthorizedPayment();

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void CaptureReportsPayPalAmounts()
    {
        var payment = NewAuthorizedPayment();

        payment.MarkCaptured("CAPTURE-1", 47.50m, 1.67m, 45.83m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAPTURE-1", payment.CaptureId);
        Assert.Equal(47.50m, payment.CapturedAmount);
        Assert.Equal(1.67m, payment.PayPalFee);
        Assert.Equal(45.83m, payment.NetAmount);
        Assert.Equal(47.50m, payment.RefundableAmount);
    }

    [Fact]
    public void CannotCaptureTwice()
    {
        var payment = NewAuthorizedPayment();
        payment.MarkCaptured("CAPTURE-1", 47.50m, 1.67m, 45.83m);

        Assert.Throws<PaymentConflictException>(() => payment.MarkCaptured("CAPTURE-2", 47.50m, 1.67m, 45.83m));
    }

    [Fact]
    public void PartialRefundsNeverExceedCapturedAmount()
    {
        var payment = NewAuthorizedPayment();
        payment.MarkCaptured("CAPTURE-1", 47.50m, 1.67m, 45.83m);

        payment.AddRefund("REFUND-1", 10.00m, "key-1", "COMPLETED");
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(37.50m, payment.RefundableAmount);

        payment.AddRefund("REFUND-2", 20.00m, "key-2", "COMPLETED");
        Assert.Equal(17.50m, payment.RefundableAmount);

        Assert.Throws<PaymentConflictException>(() => payment.AddRefund("REFUND-3", 17.51m, "key-3", "COMPLETED"));

        payment.AddRefund("REFUND-3", 17.50m, "key-3", "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
        Assert.Equal(47.50m, payment.TotalRefunded);
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = NewAuthorizedPayment();

        Assert.Throws<PaymentConflictException>(() => payment.AddRefund("REFUND-1", 10.00m, "key-1", "COMPLETED"));
    }

    [Fact]
    public void VoidReleasesTheHold()
    {
        var payment = NewAuthorizedPayment();

        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Throws<PaymentConflictException>(() => payment.MarkCaptured("CAPTURE-1", 47.50m, null, null));
    }

    [Fact]
    public void RenewAuthorizationReplacesHold()
    {
        var payment = NewAuthorizedPayment();
        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);

        payment.RenewAuthorization("AUTH-2", "CREATED", newExpiry);

        Assert.Equal("AUTH-2", payment.AuthorizationId);
        Assert.Equal(newExpiry, payment.AuthorizationExpiresAt);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Fact]
    public void ExpiredAuthorizationCannotBeCaptured()
    {
        var payment = NewAuthorizedPayment();

        payment.MarkAuthorizationExpired();

        Assert.Equal(PaymentStatus.AuthorizationExpired, payment.Status);
        Assert.Throws<PaymentConflictException>(() => payment.MarkCaptured("CAPTURE-1", 47.50m, null, null));
        Assert.Throws<PaymentConflictException>(() => payment.MarkVoided());
    }
}
