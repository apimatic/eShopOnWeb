using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentTests
{
    private const string BuyerId = "buyer@example.com";

    private static Payment NewPayment(decimal amount = 47.50m) => new(orderId: 1, BuyerId, "USD", amount);

    private static Payment Authorized(decimal amount = 47.50m)
    {
        var payment = NewPayment(amount);
        payment.BeginAuthorization();
        payment.SetAuthorized("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);
        return payment;
    }

    private static Payment Captured(decimal amount = 47.50m, decimal? fee = 1.72m, decimal? net = 45.78m)
    {
        var payment = Authorized(amount);
        payment.BeginCapture();
        payment.SetCaptured("CAP-1", "COMPLETED", amount, fee, net);
        return payment;
    }

    [Fact]
    public void StartsPendingPayment()
    {
        var payment = NewPayment();
        Assert.Equal(PaymentStatus.PendingPayment, payment.Status);
    }

    [Fact]
    public void BeginAuthorization_ReturnsSameKeyOnRetry()
    {
        var payment = NewPayment();
        var first = payment.BeginAuthorization();
        var second = payment.BeginAuthorization();
        Assert.Equal(first, second);
        Assert.False(string.IsNullOrEmpty(first));
    }

    [Fact]
    public void SetAuthorized_MovesToAuthorized()
    {
        var payment = Authorized();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal("PP-ORDER", payment.PayPalOrderId);
    }

    [Fact]
    public void BeginCapture_ThrowsWhenNotAuthorized()
    {
        var payment = NewPayment();
        Assert.Throws<PaymentConflictException>(() => payment.BeginCapture());
    }

    [Fact]
    public void SetCaptured_RecordsGrossFeeNet()
    {
        var payment = Captured();
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(47.50m, payment.CapturedGrossAmount);
        Assert.Equal(1.72m, payment.PayPalFeeAmount);
        Assert.Equal(45.78m, payment.NetAmount);
    }

    [Fact]
    public void MarkCancelled_FromAuthorized_Succeeds()
    {
        var payment = Authorized();
        payment.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void MarkCancelled_AfterCapture_Throws()
    {
        var payment = Captured();
        Assert.Throws<PaymentConflictException>(() => payment.MarkCancelled());
    }

    [Fact]
    public void Refund_BeforeCapture_Throws()
    {
        var payment = Authorized();
        Assert.Throws<PaymentConflictException>(() => payment.EnsureCanRefund(10m));
    }

    [Fact]
    public void Refund_BeyondCaptured_Throws()
    {
        var payment = Captured(amount: 20m);
        Assert.Throws<PaymentConflictException>(() => payment.EnsureCanRefund(25m));
    }

    [Fact]
    public void PartialRefund_MovesToPartiallyRefunded_AndReducesRemaining()
    {
        var payment = Captured(amount: 50m);
        payment.AddRefund("key-1", 20m);
        payment.RecalculateRefundState();

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(20m, payment.TotalRefunded());
        Assert.Equal(30m, payment.RefundableRemaining());
    }

    [Fact]
    public void TwoDistinctPartialRefunds_Accumulate()
    {
        var payment = Captured(amount: 50m);
        payment.AddRefund("key-1", 20m);
        payment.AddRefund("key-2", 15m);
        payment.RecalculateRefundState();

        Assert.Equal(35m, payment.TotalRefunded());
        Assert.Equal(15m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void CumulativeRefunds_CannotExceedCaptured()
    {
        var payment = Captured(amount: 50m);
        payment.AddRefund("key-1", 40m);
        // 40 already refunded, only 10 remains — a 20 refund must be rejected.
        Assert.Throws<PaymentConflictException>(() => payment.EnsureCanRefund(20m));
    }

    [Fact]
    public void FullRefund_MovesToRefunded()
    {
        var payment = Captured(amount: 50m);
        payment.AddRefund("key-1", null); // null = full remaining
        payment.RecalculateRefundState();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(50m, payment.TotalRefunded());
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByKey_ReturnsRecordedRefund()
    {
        var payment = Captured(amount: 50m);
        var refund = payment.AddRefund("key-abc", 5m);
        Assert.Same(refund, payment.FindRefundByKey("key-abc"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }

    [Fact]
    public void IsAuthorizationStale_TrueWhenExpiryPast()
    {
        var payment = NewPayment();
        payment.BeginAuthorization();
        payment.SetAuthorized("PP", "A", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-1), null);
        Assert.True(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsAuthorizationStale_FalseWhenExpiryFuture()
    {
        var payment = Authorized();
        Assert.False(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }
}
