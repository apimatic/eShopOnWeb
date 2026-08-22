using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void MarkAuthorizedThenCapturedPersistsFeeAndNet()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(new AuthorizationHold("po", "auth1", "CREATED", 3.69m, "USD", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow), "idem-a");
        order.MarkCaptured(new CaptureDetails("cap1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD"), "idem-c");

        Assert.Equal(OrderPaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PaypalFee);
        Assert.Equal(3.28m, order.NetAmount);
        Assert.Equal(3.69m, order.RemainingRefundable());
    }

    [Fact]
    public void PartialRefundDoesNotExceedCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(new AuthorizationHold("po", "auth1", "CREATED", 3.69m, "USD", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow), "idem-a");
        order.MarkCaptured(new CaptureDetails("cap1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD"), "idem-c");

        order.RecordRefund(new RefundDetails("r1", "COMPLETED", 1.00m), "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        order.RecordRefund(new RefundDetails("r2", "COMPLETED", 2.69m), "key-2");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void DuplicateRefundIdempotencyKeyIsFound()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(new AuthorizationHold("po", "auth1", "CREATED", 3.69m, "USD", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow), "idem-a");
        order.MarkCaptured(new CaptureDetails("cap1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD"), "idem-c");
        order.RecordRefund(new RefundDetails("r1", "COMPLETED", 1.00m), "same-key");

        var existing = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(existing);
        Assert.Equal("r1", existing!.PayPalRefundId);
    }

    [Fact]
    public void CancelAfterCaptureIsRejected()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(new AuthorizationHold("po", "auth1", "CREATED", 3.69m, "USD", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow), "idem-a");
        order.MarkCaptured(new CaptureDetails("cap1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD"), "idem-c");

        Assert.Throws<PaymentException>(() => order.MarkCancelled("idem-v"));
    }

    [Fact]
    public void AuthorizationPastThirtyDaysCannotBeRenewed()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized(new AuthorizationHold("po", "auth1", "CREATED", 3.69m, "USD", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(-31)), "idem-a");

        Assert.True(order.AuthorizationPastRenewalWindow(DateTimeOffset.UtcNow));
        Assert.True(order.AuthorizationNeedsRenewal(DateTimeOffset.UtcNow));
    }
}
