using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewPayment(decimal amount = 47.50m) =>
        new(orderId: 1, buyerId: "demouser@microsoft.com", amount: amount, currencyCode: "USD");

    [Fact]
    public void NewPayment_IsAwaitingPayment_WithDistinctStableKeys()
    {
        var p = NewPayment();

        Assert.Equal(PaymentStatus.AwaitingPayment, p.Status);
        Assert.False(string.IsNullOrEmpty(p.CreateRequestId));
        Assert.False(string.IsNullOrEmpty(p.AuthorizeRequestId));
        Assert.False(string.IsNullOrEmpty(p.CaptureRequestId));
        Assert.NotEqual(p.CreateRequestId, p.AuthorizeRequestId);
        Assert.NotEqual(p.AuthorizeRequestId, p.CaptureRequestId);
    }

    [Fact]
    public void MarkAuthorized_SetsAuthorizedState()
    {
        var p = NewPayment();
        var expiry = DateTimeOffset.UtcNow.AddDays(29);

        p.RecordPayPalOrder("PPORDER1");
        p.MarkAuthorized("AUTH1", "CREATED", expiry);

        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("PPORDER1", p.PayPalOrderId);
        Assert.Equal("AUTH1", p.AuthorizationId);
        Assert.Equal("CREATED", p.AuthorizationStatus);
        Assert.Equal(expiry, p.AuthorizationExpiresAt);
    }

    [Fact]
    public void MarkCaptured_RecordsBreakdown()
    {
        var p = NewPayment();
        p.MarkAuthorized("AUTH1", "CREATED", null);

        p.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("CAP1", p.CaptureId);
        Assert.Equal(47.50m, p.CapturedAmount);
        Assert.Equal(1.72m, p.PayPalFee);
        Assert.Equal(45.78m, p.NetAmount);
        Assert.Equal(47.50m, p.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_Partial_MovesToPartiallyRefunded_AndTracksTotals()
    {
        var p = NewPayment();
        p.MarkAuthorized("AUTH1", "CREATED", null);
        p.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        p.AddRefund("REF1", 10m, "COMPLETED", "key-a");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10m, p.TotalRefunded());
        Assert.Equal(37.50m, p.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_UpToCaptured_MovesToRefunded()
    {
        var p = NewPayment();
        p.MarkAuthorized("AUTH1", "CREATED", null);
        p.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);

        p.AddRefund("REF1", 40m, "COMPLETED", "key-a");
        p.AddRefund("REF2", 7.50m, "COMPLETED", "key-b");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(47.50m, p.TotalRefunded());
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsMatchingRefund()
    {
        var p = NewPayment();
        p.MarkAuthorized("AUTH1", "CREATED", null);
        p.MarkCaptured("CAP1", "COMPLETED", 47.50m, 1.72m, 45.78m);
        p.AddRefund("REF1", 10m, "COMPLETED", "key-a");

        Assert.NotNull(p.FindRefundByIdempotencyKey("key-a"));
        Assert.Equal("REF1", p.FindRefundByIdempotencyKey("key-a")!.PayPalRefundId);
        Assert.Null(p.FindRefundByIdempotencyKey("key-x"));
    }

    [Fact]
    public void MarkVoided_SetsVoidedState()
    {
        var p = NewPayment();
        p.MarkAuthorized("AUTH1", "CREATED", null);

        p.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
    }
}
