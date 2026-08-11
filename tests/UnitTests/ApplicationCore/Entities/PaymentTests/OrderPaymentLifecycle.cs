using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentLifecycle
{
    private static OrderPayment NewPayment(decimal amount = 29.00m) =>
        new(orderId: 1, buyerId: "buyer@example.com", amount: amount, currencyCode: "USD");

    private static OrderPayment Authorized(decimal amount = 29.00m)
    {
        var p = NewPayment(amount);
        p.MarkAuthorized("PPORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA ending 1111", "ESHOP-1-abcd1234");
        return p;
    }

    private static OrderPayment Captured(decimal amount = 29.00m)
    {
        var p = Authorized(amount);
        p.MarkCaptured("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return p;
    }

    [Fact]
    public void NewPaymentStartsAwaitingPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, NewPayment().Status);
    }

    [Fact]
    public void AuthorizeStoresPayPalStateAndTransitions()
    {
        var p = Authorized();
        Assert.Equal(OrderPaymentStatus.Authorized, p.Status);
        Assert.True(p.IsAuthorized);
        Assert.Equal("AUTH1", p.AuthorizationId);
        Assert.Equal("PPORDER1", p.PayPalOrderId);
        Assert.Equal("ESHOP-1-abcd1234", p.InvoiceId);
    }

    [Fact]
    public void CaptureBeforeAuthorizeThrows()
    {
        var p = NewPayment();
        Assert.Throws<PaymentException>(() => p.MarkCaptured("CAP1", "COMPLETED", 29m, 1m, 28m));
    }

    [Fact]
    public void CaptureRecordsFeeAndNet()
    {
        var p = Captured();
        Assert.Equal(OrderPaymentStatus.Fulfilled, p.Status);
        Assert.Equal(29.00m, p.CapturedGross);
        Assert.Equal(1.24m, p.PayPalFee);
        Assert.Equal(27.76m, p.NetAmount);
        Assert.True(p.IsCaptured);
    }

    [Fact]
    public void VoidBeforeFulfilmentCancels_AndIsIdempotent()
    {
        var p = Authorized();
        p.MarkVoided();
        Assert.Equal(OrderPaymentStatus.Canceled, p.Status);
        p.MarkVoided(); // idempotent, no throw
        Assert.Equal(OrderPaymentStatus.Canceled, p.Status);
    }

    [Fact]
    public void VoidAfterCaptureThrows()
    {
        var p = Captured();
        Assert.Throws<PaymentException>(() => p.MarkVoided());
    }

    [Fact]
    public void PartialThenFullRefundTransitionsCorrectly()
    {
        var p = Captured(20.00m);
        p.AddRefund("R1", 5.00m, "COMPLETED", "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(5.00m, p.TotalRefunded());
        Assert.Equal(15.00m, p.RefundableRemaining());

        p.AddRefund("R2", 15.00m, "COMPLETED", "key-2");
        Assert.Equal(OrderPaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void OverRefundThrows()
    {
        var p = Captured(20.00m);
        Assert.Throws<PaymentException>(() => p.AddRefund("R1", 25.00m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void RefundBeforeCaptureThrows()
    {
        var p = Authorized();
        Assert.Throws<PaymentException>(() => p.AddRefund("R1", 1.00m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var p = Captured(20.00m);
        p.AddRefund("R1", 5.00m, "COMPLETED", "key-1");
        Assert.NotNull(p.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(p.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void StaleAuthorizationIsDetected()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER1", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1), null, "inv");
        Assert.True(p.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }
}
