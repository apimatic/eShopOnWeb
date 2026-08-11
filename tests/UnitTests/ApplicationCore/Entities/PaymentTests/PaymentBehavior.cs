using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment NewAuthorized(decimal amount = 100m) =>
        new(amount, "USD", "PPORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111", usedSavedCard: false);

    [Fact]
    public void NewPayment_IsAuthorized()
    {
        var p = NewAuthorized();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("AUTH1", p.AuthorizationId);
        Assert.Null(p.CaptureId);
    }

    [Fact]
    public void MarkCaptured_RecordsFeeBreakdown_AndStatus()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3.20m, 96.80m);

        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("CAP1", p.CaptureId);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3.20m, p.PayPalFee);
        Assert.Equal(96.80m, p.NetAmount);
        Assert.Equal(100m, p.RefundableRemaining);
    }

    [Fact]
    public void MarkCaptured_WhenNotAuthorized_Throws()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3m, 97m);
        Assert.Throws<InvalidOperationException>(() => p.MarkCaptured("CAP2", "COMPLETED", 100m, 3m, 97m));
    }

    [Fact]
    public void PartialRefund_SetsPartiallyRefunded_AndReducesRemaining()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3m, 97m);

        var refund = new PaymentRefund("key-1", 40m, "USD");
        refund.SetResult("REF1", "COMPLETED");
        p.AddRefund(refund);

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded);
        Assert.Equal(60m, p.RefundableRemaining);
    }

    [Fact]
    public void RefundsUpToCapturedAmount_SetsRefunded()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3m, 97m);

        foreach (var (key, amt) in new[] { ("k1", 60m), ("k2", 40m) })
        {
            var r = new PaymentRefund(key, amt, "USD");
            r.SetResult("REF-" + key, "COMPLETED");
            p.AddRefund(r);
        }

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(100m, p.TotalRefunded);
        Assert.Equal(0m, p.RefundableRemaining);
    }

    [Fact]
    public void PendingRefund_CountsTowardRemaining_SoItCannotBeOverRefunded()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3m, 97m);

        var pending = new PaymentRefund("k1", 30m, "USD");
        pending.SetResult("REF1", "PENDING");
        p.AddRefund(pending);

        Assert.Equal(30m, p.TotalRefunded);
        Assert.Equal(70m, p.RefundableRemaining);
    }

    [Fact]
    public void Void_WhenAuthorized_SetsVoided()
    {
        var p = NewAuthorized();
        p.MarkVoided();
        Assert.Equal(PaymentStatus.Voided, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
    }

    [Fact]
    public void Void_AfterCapture_Throws()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP1", "COMPLETED", 100m, 3m, 97m);
        Assert.Throws<InvalidOperationException>(() => p.MarkVoided());
    }

    [Fact]
    public void ApplyReauthorization_ReplacesAuthorizationId()
    {
        var p = NewAuthorized();
        var newExpiry = DateTimeOffset.UtcNow.AddDays(29);
        p.ApplyReauthorization("AUTH2", "CREATED", newExpiry);

        Assert.Equal("AUTH2", p.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal(newExpiry, p.AuthorizationExpiresAt);
    }

    [Fact]
    public void Order_WithoutPayment_IsAwaitingPayment()
    {
        var order = new Order("buyer@example.com",
            new Address("1 St", "City", "ST", "US", "00000"),
            new System.Collections.Generic.List<OrderItem>());
        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
    }
}
