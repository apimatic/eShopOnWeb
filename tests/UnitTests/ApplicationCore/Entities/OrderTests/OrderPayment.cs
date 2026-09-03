using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    private static Order CreateOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = CreateOrder();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void RecordsAuthorizationOnce()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordAuthorization("paypal-order-2", "auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("auth-1", order.PayPalAuthorizationId);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public void CaptureRecordsFeeAndNet()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordCapture("cap-1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PaypalFee);
        Assert.Equal(3.28m, order.NetAmount);
        Assert.Equal("cap-1", order.PayPalCaptureId);
    }

    [Fact]
    public void CancelReleasesHoldBeforeFulfilment()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.Cancel("VOIDED");

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
        order.Cancel("VOIDED");
        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
    }

    [Fact]
    public void FulfilledOrderCannotBeCancelled()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordCapture("cap-1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");

        Assert.Throws<InvalidOrderStateException>(() => order.Cancel("VOIDED"));
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapturedAmount()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordCapture("cap-1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");

        var first = order.RecordRefund("rf-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());
        Assert.Same(first, order.FindRefundByIdempotencyKey("key-1"));

        Assert.Throws<InvalidOrderStateException>(() =>
            order.RecordRefund("rf-too-much", "COMPLETED", 2.70m, "USD", "key-2"));

        order.RecordRefund("rf-2", "COMPLETED", 2.69m, "USD", "key-2");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void SameIdempotencyKeyFindsExistingRefund()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        order.RecordCapture("cap-1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");
        order.RecordRefund("rf-1", "COMPLETED", 1.00m, "USD", "repeat-key");

        var existing = order.FindRefundByIdempotencyKey("repeat-key");
        Assert.NotNull(existing);
        Assert.Equal("rf-1", existing!.PayPalRefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void ShopperCannotActOnAnotherBuyersOrder()
    {
        var order = CreateOrder();
        Assert.Throws<ForbiddenResourceException>(() => order.EnsureOwnedBy("someone-else"));
        order.EnsureOwnedBy(new OrderBuilder().TestBuyerId);
    }
}
