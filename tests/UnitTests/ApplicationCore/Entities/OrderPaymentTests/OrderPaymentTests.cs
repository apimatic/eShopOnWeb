using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewCaptured(decimal amount = 100m)
    {
        var p = new OrderPayment(1, "buyer@x.com", "USD", amount);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        p.MarkCaptured("CAP1", "COMPLETED", amount, 3m, amount - 3m);
        return p;
    }

    [Fact]
    public void NewPayment_StartsPendingWithUniqueReference()
    {
        var p1 = new OrderPayment(1, "b", "USD", 10m);
        var p2 = new OrderPayment(1, "b", "USD", 10m);
        Assert.Equal(PaymentStatus.PendingPayment, p1.Status);
        Assert.NotEqual(p1.PaymentReference, p2.PaymentReference);
    }

    [Fact]
    public void MarkAuthorized_ThenCaptured_RecordsProceeds()
    {
        var p = NewCaptured(50m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal(50m, p.CapturedAmount);
        Assert.Equal(3m, p.PayPalFee);
        Assert.Equal(47m, p.NetAmount);
        Assert.Equal(50m, p.RefundableRemaining());
    }

    [Fact]
    public void PartialRefund_MovesToPartiallyRefunded_AndReducesRemaining()
    {
        var p = NewCaptured(100m);
        p.AddRefund(new PaymentRefund("R1", 30m, "key-1", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(30m, p.TotalRefunded());
        Assert.Equal(70m, p.RefundableRemaining());
    }

    [Fact]
    public void RefundsSummingToCapture_MovesToRefunded()
    {
        var p = NewCaptured(100m);
        p.AddRefund(new PaymentRefund("R1", 60m, "key-1", "COMPLETED"));
        p.AddRefund(new PaymentRefund("R2", 40m, "key-2", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void Refund_ExceedingRemaining_Throws_NeverExceedsCaptured()
    {
        var p = NewCaptured(100m);
        p.AddRefund(new PaymentRefund("R1", 80m, "key-1", "COMPLETED"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => p.AddRefund(new PaymentRefund("R2", 30m, "key-2", "COMPLETED")));
        Assert.Contains("exceeds", ex.Message);
        // Nothing over-refunded.
        Assert.Equal(80m, p.TotalRefunded());
        Assert.Equal(20m, p.RefundableRemaining());
    }

    [Fact]
    public void Refund_BeforeCapture_Throws()
    {
        var p = new OrderPayment(1, "b", "USD", 100m);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        Assert.Throws<InvalidOperationException>(
            () => p.AddRefund(new PaymentRefund("R1", 10m, "key-1", "COMPLETED")));
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExisting()
    {
        var p = NewCaptured(100m);
        var refund = new PaymentRefund("R1", 25m, "the-key", "COMPLETED");
        p.AddRefund(refund);

        Assert.Same(refund, p.FindRefundByIdempotencyKey("the-key"));
        Assert.Null(p.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void MarkVoided_SetsVoided()
    {
        var p = new OrderPayment(1, "b", "USD", 100m);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        p.MarkVoided();
        Assert.Equal(PaymentStatus.Voided, p.Status);
    }

    [Fact]
    public void MarkAuthorizationRenewed_UpdatesAuthorizationId()
    {
        var p = new OrderPayment(1, "b", "USD", 100m);
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddSeconds(-1));
        p.MarkAuthorizationRenewed("AUTH2", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        Assert.Equal("AUTH2", p.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, p.Status);
    }
}
