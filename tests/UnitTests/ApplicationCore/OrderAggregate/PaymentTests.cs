using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.OrderAggregate;

public class PaymentTests
{
    private static Payment NewPayment(decimal amount = 100m) => new(amount, "USD");

    private static Payment Authorized(decimal amount = 100m)
    {
        var p = NewPayment(amount);
        p.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.Now.AddDays(29));
        return p;
    }

    private static Payment Captured(decimal amount = 100m)
    {
        var p = Authorized(amount);
        p.MarkCaptured("CAP-1", "COMPLETED", amount, 3.20m, amount - 3.20m);
        return p;
    }

    [Fact]
    public void New_payment_awaits_authorization_with_unique_seed()
    {
        var a = NewPayment();
        var b = NewPayment();

        Assert.Equal(PaymentStatus.PendingAuthorization, a.Status);
        Assert.True(a.IsAwaitingPayment);
        Assert.NotEqual(Guid.Empty, a.IdempotencySeed);
        Assert.NotEqual(a.IdempotencySeed, b.IdempotencySeed);
    }

    [Fact]
    public void MarkAuthorized_moves_to_authorized_and_records_paypal_state()
    {
        var p = NewPayment();
        var expiry = DateTimeOffset.Now.AddDays(29);

        p.MarkAuthorized("PPO", "AUTH", "CREATED", expiry);

        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("PPO", p.PayPalOrderId);
        Assert.Equal("AUTH", p.AuthorizationId);
        Assert.Equal(expiry, p.AuthorizationExpiresAt);
    }

    [Fact]
    public void Cannot_capture_before_authorization()
    {
        var p = NewPayment();
        Assert.Throws<PaymentOperationException>(() => p.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m));
    }

    [Fact]
    public void Capture_records_amount_fee_and_net()
    {
        var p = Authorized();
        p.MarkCaptured("CAP", "COMPLETED", 100m, 3.20m, 96.80m);

        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3.20m, p.PayPalFee);
        Assert.Equal(96.80m, p.NetAmount);
        Assert.True(p.IsCaptured);
    }

    [Fact]
    public void Void_only_valid_while_authorized()
    {
        var captured = Captured();
        Assert.Throws<PaymentOperationException>(() => captured.MarkVoided());

        var authorized = Authorized();
        authorized.MarkVoided();
        Assert.Equal(PaymentStatus.Voided, authorized.Status);
    }

    [Fact]
    public void Refund_cannot_exceed_captured_amount()
    {
        var p = Captured(100m);
        Assert.Throws<PaymentOperationException>(() => p.StartRefund("k1", 150m));
    }

    [Fact]
    public void Partial_then_remaining_refund_marks_fully_refunded()
    {
        var p = Captured(100m);

        var r1 = p.StartRefund("k1", 40m);
        p.CompleteRefund(r1, "RF-1", RefundStatus.Completed);
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded);
        Assert.Equal(60m, p.RefundableRemaining);

        var r2 = p.StartRefund("k2", 60m);
        p.CompleteRefund(r2, "RF-2", RefundStatus.Completed);
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining);

        // A third refund of anything is now impossible.
        Assert.Throws<PaymentOperationException>(() => p.StartRefund("k3", 0.01m));
    }

    [Fact]
    public void A_pending_refund_still_reserves_the_amount()
    {
        var p = Captured(100m);
        var r1 = p.StartRefund("k1", 100m); // pending, not yet completed

        Assert.Equal(100m, p.TotalRefunded);
        Assert.Equal(0m, p.RefundableRemaining);
        Assert.Throws<PaymentOperationException>(() => p.StartRefund("k2", 1m));
    }

    [Fact]
    public void FindRefundByKey_returns_prior_refund()
    {
        var p = Captured(100m);
        var r = p.StartRefund("dup-key", 10m);

        Assert.Same(r, p.FindRefundByKey("dup-key"));
        Assert.Null(p.FindRefundByKey("other-key"));
    }

    [Fact]
    public void Full_refund_uses_remaining_when_amount_omitted()
    {
        var p = Captured(100m);
        var r = p.StartRefund("k1", null);
        Assert.Equal(100m, r.Amount);
    }
}
