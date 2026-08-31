using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private readonly int _orderId = 1;
    private readonly string _buyerId = "buyer@example.com";

    private OrderPayment NewCapturedPayment(decimal amount = 47.50m)
    {
        var payment = new OrderPayment(_orderId, _buyerId, amount, "USD");
        payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        payment.RecordCapture("PP-CAPTURE-1", "COMPLETED", amount, 1.72m, amount - 1.72m, DateTimeOffset.UtcNow);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 10m, "USD");

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(10m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.False(string.IsNullOrEmpty(payment.InvoiceId));
    }

    [Fact]
    public void RecordAuthorizationCarriesPayPalState()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 10m, "USD");
        var expires = DateTimeOffset.UtcNow.AddDays(29);

        payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", expires);

        Assert.Equal(OrderPaymentStatus.Authorized, payment.Status);
        Assert.Equal("PP-ORDER-1", payment.PayPalOrderId);
        Assert.Equal("PP-AUTH-1", payment.AuthorizationId);
        Assert.Equal("CREATED", payment.AuthorizationStatus);
        Assert.Equal(expires, payment.AuthorizationExpiresAt);
        Assert.NotNull(payment.AuthorizedAt);
    }

    [Fact]
    public void RecordCaptureStoresFeeBreakdown()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 47.50m, "USD");
        payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));

        payment.RecordCapture("PP-CAPTURE-1", "COMPLETED", 47.50m, 1.72m, 45.78m, DateTimeOffset.UtcNow);

        Assert.Equal(OrderPaymentStatus.Captured, payment.Status);
        Assert.Equal("PP-CAPTURE-1", payment.CaptureId);
        Assert.Equal(47.50m, payment.CapturedAmount);
        Assert.Equal(1.72m, payment.PayPalFee);
        Assert.Equal(45.78m, payment.NetAmount);
    }

    [Fact]
    public void CaptureBeforeAuthorizationThrows()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 10m, "USD");

        Assert.Throws<OrderStateException>(() =>
            payment.RecordCapture("PP-CAPTURE-1", "COMPLETED", 10m, null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void VoidReleasesHold()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 10m, "USD");
        payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));

        payment.MarkVoided("VOIDED");

        Assert.Equal(OrderPaymentStatus.Voided, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void VoidAfterCaptureThrows()
    {
        var payment = NewCapturedPayment();

        Assert.Throws<OrderStateException>(() => payment.MarkVoided("VOIDED"));
    }

    [Fact]
    public void PartialRefundsNeverExceedCapturedAmount()
    {
        var payment = NewCapturedPayment(47.50m);

        payment.AddRefund("PP-REF-1", "COMPLETED", 20m, "key-1", null);
        payment.AddRefund("PP-REF-2", "COMPLETED", 27.50m, "key-2", null);

        Assert.Equal(0m, payment.RefundableAmount);
        Assert.Throws<OrderStateException>(() =>
            payment.AddRefund("PP-REF-3", "COMPLETED", 0.01m, "key-3", null));
    }

    [Fact]
    public void RefundBeyondCapturedThrows()
    {
        var payment = NewCapturedPayment(47.50m);

        Assert.Throws<OrderStateException>(() =>
            payment.AddRefund("PP-REF-1", "COMPLETED", 47.51m, "key-1", null));
    }

    [Fact]
    public void RefundBeforeCaptureThrows()
    {
        var payment = new OrderPayment(_orderId, _buyerId, 10m, "USD");
        payment.RecordAuthorization("PP-ORDER-1", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));

        Assert.Throws<OrderStateException>(() =>
            payment.AddRefund("PP-REF-1", "COMPLETED", 5m, "key-1", null));
    }

    [Fact]
    public void DuplicateIdempotencyKeyThrows()
    {
        var payment = NewCapturedPayment();
        payment.AddRefund("PP-REF-1", "COMPLETED", 10m, "same-key", null);

        Assert.Throws<DuplicateException>(() =>
            payment.AddRefund("PP-REF-2", "COMPLETED", 10m, "same-key", null));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsRecordedRefund()
    {
        var payment = NewCapturedPayment();
        payment.AddRefund("PP-REF-1", "COMPLETED", 10m, "key-1", null);

        var found = payment.FindRefundByIdempotencyKey("key-1");

        Assert.NotNull(found);
        Assert.Equal("PP-REF-1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void FailedRefundsDoNotReduceRefundableAmount()
    {
        var payment = NewCapturedPayment(47.50m);
        payment.AddRefund("PP-REF-1", "FAILED", 10m, "key-1", null);

        Assert.Equal(47.50m, payment.RefundableAmount);
    }
}
