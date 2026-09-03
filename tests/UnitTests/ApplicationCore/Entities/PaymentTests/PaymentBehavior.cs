using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment NewPayment() => new(orderId: 5, buyerId: "buyer-1", currencyCode: "USD", amount: 47.50m, invoiceId: "ESHOP-5-abc");

    [Fact]
    public void StartsAwaitingPayment()
    {
        var p = NewPayment();
        Assert.Equal(PaymentStatus.AwaitingPayment, p.Status);
        Assert.Equal(0m, p.TotalRefunded);
        Assert.Equal(0m, p.RemainingRefundable);
    }

    [Fact]
    public void AuthorizeThenFulfilTracksPayPalState()
    {
        var p = NewPayment();
        p.MarkAuthorized("PP-ORDER", "AUTH-1", DateTimeOffset.UtcNow.AddDays(29));
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("AUTH-1", p.AuthorizationId);

        p.MarkFulfilled("CAP-1", capturedGross: 47.50m, payPalFee: 1.72m, netAmount: 45.78m);
        Assert.Equal(PaymentStatus.Fulfilled, p.Status);
        Assert.Equal("CAP-1", p.CaptureId);
        Assert.Equal(47.50m, p.CapturedGross);
        Assert.Equal(45.78m, p.NetAmount);
        Assert.Equal(47.50m, p.RemainingRefundable);
    }

    [Fact]
    public void PartialThenFullRefundMovesStatusAndCannotExceedCaptured()
    {
        var p = NewPayment();
        p.MarkAuthorized("PP", "AUTH", null);
        p.MarkFulfilled("CAP", 47.50m, 1.72m, 45.78m);

        p.AddRefund(new PaymentRefund("k1", "R1", 10m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10m, p.TotalRefunded);
        Assert.Equal(37.50m, p.RemainingRefundable);

        p.AddRefund(new PaymentRefund("k2", "R2", 37.50m, "COMPLETED"));
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(47.50m, p.TotalRefunded);
        Assert.Equal(0m, p.RemainingRefundable);
    }

    [Fact]
    public void FindRefundByKeyReturnsExistingRefund()
    {
        var p = NewPayment();
        p.MarkAuthorized("PP", "AUTH", null);
        p.MarkFulfilled("CAP", 47.50m, 1.72m, 45.78m);
        var refund = new PaymentRefund("dup-key", "R1", 5m, "COMPLETED");
        p.AddRefund(refund);

        Assert.Same(refund, p.FindRefundByKey("dup-key"));
        Assert.Null(p.FindRefundByKey("other-key"));
    }

    [Fact]
    public void CancelMovesToCancelled()
    {
        var p = NewPayment();
        p.MarkAuthorized("PP", "AUTH", null);
        p.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, p.Status);
    }
}
