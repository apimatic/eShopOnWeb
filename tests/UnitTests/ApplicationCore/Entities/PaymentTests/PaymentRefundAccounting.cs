using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundAccounting
{
    private static Payment CapturedPayment(decimal amount = 47.50m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@test", amount: amount, currencyCode: "USD");
        payment.MarkAuthorized("po-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), paymentMethodId: null);
        payment.MarkCaptured("cap-1", "COMPLETED", amount, payPalFee: 1.72m, netAmount: amount - 1.72m);
        return payment;
    }

    [Fact]
    public void NewPaymentAwaitsPaymentAndIsNotRefundable()
    {
        var payment = new Payment(1, "buyer@test", 10m, "USD");

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void CapturedPaymentIsFullyRefundable()
    {
        var payment = CapturedPayment();

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(47.50m, payment.RefundableRemaining);
    }

    [Fact]
    public void PartialRefundReducesRemainingAndSetsPartiallyRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-a", "refund-a", 10m, "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.TotalRefunded);
        Assert.Equal(37.50m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundingTheRemainderSetsRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-a", "refund-a", 10m, "COMPLETED");
        payment.AddRefund("key-b", "refund-b", 37.50m, "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FailedRefundsDoNotCountTowardTheRefundedTotal()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-a", "refund-a", 10m, "FAILED");

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(47.50m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundLookupByKeyFindsTheRecordedRefund()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-a", "refund-a", 10m, "COMPLETED");

        Assert.True(payment.HasRefundWithKey("key-a"));
        Assert.False(payment.HasRefundWithKey("key-x"));
        Assert.Equal("refund-a", payment.GetRefundByKey("key-a")!.PayPalRefundId);
    }

    [Fact]
    public void AuthorizationStalenessTracksExpiry()
    {
        var payment = new Payment(1, "buyer@test", 10m, "USD");
        payment.MarkAuthorized("po", "auth", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-1), null);

        Assert.True(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));

        payment.UpdateAuthorization("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        Assert.False(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RotatingTheAuthorizeKeyChangesIt()
    {
        var payment = new Payment(1, "buyer@test", 10m, "USD");
        var original = payment.AuthorizeRequestId;

        payment.RotateAuthorizeRequestId();

        Assert.NotEqual(original, payment.AuthorizeRequestId);
    }
}
