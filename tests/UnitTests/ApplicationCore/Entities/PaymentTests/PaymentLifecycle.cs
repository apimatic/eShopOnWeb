using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 100m) => new(orderId: 1, amount: amount, currency: "USD");

    private static Payment AuthorizedPayment(decimal amount = 100m)
    {
        var payment = NewPayment(amount);
        payment.MarkAuthorized("paypal-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), paymentMethodId: null);
        return payment;
    }

    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = AuthorizedPayment(amount);
        payment.MarkCaptured("capture-1", "COMPLETED", amount, feeAmount: 3m, netAmount: amount - 3m);
        return payment;
    }

    [Fact]
    public void NewPaymentIsAwaitingAuthorization()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.AwaitingAuthorization, payment.Status);
    }

    [Fact]
    public void MarkAuthorizedSetsStatusAndFields()
    {
        var payment = AuthorizedPayment();

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("paypal-order-1", payment.PayPalOrderId);
        Assert.Equal("auth-1", payment.PayPalAuthorizationId);
    }

    [Fact]
    public void MarkCapturedWithoutAuthorizationThrows()
    {
        var payment = NewPayment();

        Assert.Throws<InvalidOrderStateException>(() => payment.MarkCaptured("capture-1", "COMPLETED", 100m, 3m, 97m));
    }

    [Fact]
    public void MarkCapturedFromAuthorizedSetsCapturedFields()
    {
        var payment = CapturedPayment();

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("capture-1", payment.PayPalCaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3m, payment.PayPalFeeAmount);
        Assert.Equal(97m, payment.NetAmount);
    }

    [Fact]
    public void MarkVoidedFromAuthorizedSucceeds()
    {
        var payment = AuthorizedPayment();

        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }

    [Fact]
    public void MarkVoidedWithoutAuthorizationThrows()
    {
        var payment = NewPayment();

        Assert.Throws<InvalidOrderStateException>(() => payment.MarkVoided());
    }

    [Fact]
    public void MarkReauthorizedUpdatesAuthorizationId()
    {
        var payment = AuthorizedPayment();

        payment.MarkReauthorized("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal("auth-2", payment.PayPalAuthorizationId);
    }

    [Fact]
    public void AddRefundFullAmountSetsStatusRefunded()
    {
        var payment = CapturedPayment(100m);

        var refund = payment.AddRefund("refund-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable);
        Assert.Equal("refund-1", refund.PayPalRefundId);
    }

    [Fact]
    public void AddRefundPartialAmountSetsStatusPartiallyRefunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(60m, payment.RemainingRefundable);
    }

    [Fact]
    public void AddRefundExceedingRemainingThrows()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("refund-1", 60m, "COMPLETED", "key-1");

        Assert.Throws<InvalidOrderStateException>(() => payment.AddRefund("refund-2", 60m, "COMPLETED", "key-2"));
    }

    [Fact]
    public void AddRefundSameIdempotencyKeyTwiceDoesNotDoubleRefund()
    {
        var payment = CapturedPayment(100m);

        var first = payment.AddRefund("refund-1", 40m, "COMPLETED", "key-1");
        var second = payment.AddRefund("refund-1", 40m, "COMPLETED", "key-1");

        Assert.Same(first, second);
        Assert.Single(payment.Refunds);
        Assert.Equal(60m, payment.RemainingRefundable);
    }

    [Fact]
    public void AddRefundTwoDistinctPartialRefundsBothApply()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("refund-2", 30m, "COMPLETED", "key-2");

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(70m, payment.TotalRefunded);
        Assert.Equal(30m, payment.RemainingRefundable);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void AddRefundBeforeCaptureThrows()
    {
        var payment = AuthorizedPayment(100m);

        Assert.Throws<InvalidOrderStateException>(() => payment.AddRefund("refund-1", 10m, "COMPLETED", "key-1"));
    }
}
