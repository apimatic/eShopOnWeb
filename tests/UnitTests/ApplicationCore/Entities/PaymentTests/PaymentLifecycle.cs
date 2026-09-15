using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 100m) =>
        new(orderId: 1, buyerId: "buyer@example.com", currencyCode: "USD", amount: amount);

    private static Payment Captured(decimal amount = 100m)
    {
        var payment = NewPayment(amount);
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), null);
        payment.SetCaptured("CAP-1", "COMPLETED", amount, payPalFee: 3m, netAmount: amount - 3m);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var payment = NewPayment();
        Assert.Equal(PaymentStatus.PendingPayment, payment.Status);
        Assert.NotNull(payment.Reference);
        Assert.NotEmpty(payment.Reference);
    }

    [Fact]
    public void AuthorizationMovesToAuthorized()
    {
        var payment = NewPayment();
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), savedPaymentMethodId: 7);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal(7, payment.SavedPaymentMethodId);
    }

    [Fact]
    public void CaptureRecordsFeeAndNetAndFulfils()
    {
        var payment = Captured(100m);

        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3m, payment.PayPalFee);
        Assert.Equal(97m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableAmount());
    }

    [Fact]
    public void PartialRefundLeavesPartiallyRefunded()
    {
        var payment = Captured(100m);
        payment.AddRefund(new PaymentRefund("key-1", "REF-1", 40m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.RefundedAmount());
        Assert.Equal(60m, payment.RefundableAmount());
    }

    [Fact]
    public void TwoPartialRefundsToFullBecomeRefunded()
    {
        var payment = Captured(100m);
        payment.AddRefund(new PaymentRefund("key-1", "REF-1", 40m, "USD", "COMPLETED"));
        payment.AddRefund(new PaymentRefund("key-2", "REF-2", 60m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(100m, payment.RefundedAmount());
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void FailedRefundDoesNotCountTowardRefundedTotal()
    {
        var payment = Captured(100m);
        payment.AddRefund(new PaymentRefund("key-1", "REF-1", 40m, "USD", "FAILED"));

        Assert.Equal(0m, payment.RefundedAmount());
        Assert.Equal(100m, payment.RefundableAmount());
        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
    }

    [Fact]
    public void CancelVoidsTheHold()
    {
        var payment = NewPayment();
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), null);
        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void RenewAuthorizationUpdatesTheHold()
    {
        var payment = NewPayment();
        payment.SetAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1), null);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(29);
        payment.RenewAuthorization("AUTH-2", "CREATED", newExpiry);

        Assert.Equal("AUTH-2", payment.AuthorizationId);
        Assert.Equal(newExpiry, payment.AuthorizationExpiresAt);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }
}
