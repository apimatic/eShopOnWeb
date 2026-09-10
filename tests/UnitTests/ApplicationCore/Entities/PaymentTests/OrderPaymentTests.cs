using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment Fulfilled(decimal captured = 100m)
    {
        var p = new OrderPayment(1, "buyer@test", captured, "USD");
        p.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA ending 1111");
        p.MarkFulfilled("CAPTURE-1", "COMPLETED", captured, 3m, captured - 3m);
        return p;
    }

    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithUniqueReference()
    {
        var p1 = new OrderPayment(1, "buyer@test", 10m, "USD");
        var p2 = new OrderPayment(1, "buyer@test", 10m, "USD");

        Assert.Equal(PaymentStatus.AwaitingPayment, p1.Status);
        Assert.Equal("ESHOP-1", p1.ReconciliationReference);
        Assert.NotEqual(p1.PaymentReference, p2.PaymentReference); // globally unique even for same order id
    }

    [Fact]
    public void Authorize_then_Fulfil_movesThroughStates_andRecordsFeeAndNet()
    {
        var p = Fulfilled(100m);

        Assert.Equal(PaymentStatus.Fulfilled, p.Status);
        Assert.Equal("CAPTURE-1", p.CaptureId);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3m, p.PayPalFee);
        Assert.Equal(97m, p.NetAmount);
        Assert.Equal(100m, p.RefundableRemaining());
    }

    [Fact]
    public void PartialRefund_marksPartiallyRefunded_andReducesRefundable()
    {
        var p = Fulfilled(100m);

        p.AddRefund("K1", "REFUND-1", 40m, "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded());
        Assert.Equal(60m, p.RefundableRemaining());
        Assert.NotNull(p.FindRefundByKey("K1"));
        Assert.Null(p.FindRefundByKey("other"));
    }

    [Fact]
    public void FullRefundAcrossParts_marksRefunded()
    {
        var p = Fulfilled(100m);

        p.AddRefund("K1", "REFUND-1", 40m, "COMPLETED");
        p.AddRefund("K2", "REFUND-2", 60m, "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void Cancel_marksCancelled()
    {
        var p = new OrderPayment(1, "buyer@test", 100m, "USD");
        p.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "VISA ending 1111");

        p.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
    }
}
