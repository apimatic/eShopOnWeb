using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Payment NewPayment(decimal amount = 47.50m) =>
        new(orderId: 1, buyerId: "buyer@example.com", currencyCode: "USD", amount: amount);

    private static Payment Authorized(decimal amount = 47.50m)
    {
        var p = NewPayment(amount);
        p.MarkAuthorized("ORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA ****1111", null);
        return p;
    }

    private static Payment Captured(decimal amount = 47.50m)
    {
        var p = Authorized(amount);
        p.MarkCaptured("CAP1", "COMPLETED", amount, payPalFee: 1.72m, netAmount: amount - 1.72m);
        return p;
    }

    [Fact]
    public void NewPayment_IsAwaitingPayment()
    {
        var p = NewPayment();
        Assert.Equal(PaymentStatus.AwaitingPayment, p.Status);
        Assert.False(p.IsAuthorized);
        Assert.False(p.IsCaptured);
    }

    [Fact]
    public void MarkAuthorized_SetsHoldState()
    {
        var p = Authorized();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("AUTH1", p.AuthorizationId);
        Assert.True(p.IsAuthorized);
    }

    [Fact]
    public void MarkAuthorized_Twice_Throws()
    {
        var p = Authorized();
        Assert.Throws<InvalidOperationException>(() =>
            p.MarkAuthorized("ORDER1", "AUTH2", "CREATED", null, null, null));
    }

    [Fact]
    public void MarkCaptured_RequiresAuthorized()
    {
        var p = NewPayment();
        Assert.Throws<InvalidOperationException>(() =>
            p.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m));
    }

    [Fact]
    public void MarkCaptured_RecordsFeeAndNet()
    {
        var p = Captured();
        Assert.Equal(PaymentStatus.Fulfilled, p.Status);
        Assert.Equal(47.50m, p.CapturedAmount);
        Assert.Equal(1.72m, p.PayPalFee);
        Assert.Equal(45.78m, p.NetAmount);
        Assert.Equal(47.50m, p.RefundableRemaining);
    }

    [Fact]
    public void Cancel_OnlyBeforeCapture()
    {
        var authorized = Authorized();
        authorized.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, authorized.Status);

        var captured = Captured();
        Assert.Throws<InvalidOperationException>(() => captured.MarkCancelled());
    }

    [Fact]
    public void PartialRefunds_AccumulateAndCapAtCaptured()
    {
        var p = Captured(); // 47.50 captured

        var r1 = p.AddRefund("K1", 10m);
        r1.SetResult("REF1", PaymentRefund.StatusCompleted);
        p.RecalculateAfterRefund();

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10m, p.RefundedAmount);
        Assert.Equal(37.50m, p.RefundableRemaining);

        // Over-refunding the remaining is rejected.
        Assert.Throws<InvalidOperationException>(() => p.AddRefund("K2", 40m));

        // A second legitimate partial refund is allowed.
        var r2 = p.AddRefund("K2", 37.50m);
        r2.SetResult("REF2", PaymentRefund.StatusCompleted);
        p.RecalculateAfterRefund();

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining);
    }

    [Fact]
    public void FindRefundByKey_ReturnsExisting()
    {
        var p = Captured();
        var r1 = p.AddRefund("KEY-A", 5m);
        Assert.Same(r1, p.FindRefundByKey("KEY-A"));
        Assert.Null(p.FindRefundByKey("KEY-B"));
    }

    [Fact]
    public void InvoiceReference_IsStableAndOrderScoped()
    {
        var p = NewPayment();
        Assert.StartsWith("ESHOP-ORDER-1-", p.InvoiceReference);
        Assert.Equal(p.InvoiceReference, p.InvoiceReference); // deterministic
    }
}
