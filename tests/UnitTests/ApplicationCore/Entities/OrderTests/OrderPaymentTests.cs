using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    private static Order NewOrder(decimal unitPrice = 25m, int units = 2)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test item", "http://test/1.png"), unitPrice, units);
        return new Order("buyer@example.com", new Address("1 Main St", "Seattle", "WA", "US", "98101"),
            new List<OrderItem> { item });
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void MarkAuthorizedIsIdempotentForSameAuthorization()
    {
        var order = NewOrder();

        Assert.True(order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD"));
        Assert.False(order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD"));
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public void CannotAuthorizeFulfilledOrder()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");
        order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m);

        Assert.Throws<PaymentStateException>(() => order.MarkAuthorized("PP-2", "AUTH-2", "CREATED", null, "USD"));
    }

    [Fact]
    public void CannotCaptureUnpaidOrder()
    {
        var order = NewOrder();

        Assert.Throws<PaymentStateException>(() => order.MarkCaptured("CAP-1", "COMPLETED", 50m, null, null));
    }

    [Fact]
    public void MarkCapturedIsIdempotentForSameCapture()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");

        Assert.True(order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m));
        Assert.False(order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m));
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CannotCancelFulfilledOrder()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");
        order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m);

        Assert.Throws<PaymentStateException>(() => order.MarkCancelled("VOIDED"));
    }

    [Fact]
    public void RefundsNeverExceedCapturedAmount()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");
        order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m);

        order.AddRefund("REF-1", 30m, "USD", "COMPLETED", "key-1");
        Assert.Equal(20m, order.RefundableRemaining());

        Assert.Throws<PaymentStateException>(() => order.AddRefund("REF-2", 25m, "USD", "COMPLETED", "key-2"));

        order.AddRefund("REF-2", 20m, "USD", "COMPLETED", "key-2");
        Assert.Equal(0m, order.RefundableRemaining());
        Assert.Equal("REFUNDED", order.CaptureStatus);
    }

    [Fact]
    public void RefundIdempotencyKeyReturnsOriginalRefund()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");
        order.MarkCaptured("CAP-1", "COMPLETED", 50m, 1.81m, 48.19m);
        order.AddRefund("REF-1", 10m, "USD", "COMPLETED", "key-1");

        var replay = order.FindRefundByIdempotencyKey("key-1");

        Assert.NotNull(replay);
        Assert.Equal("REF-1", replay!.RefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CannotRefundUnfulfilledOrder()
    {
        var order = NewOrder();
        order.MarkAuthorized("PP-1", "AUTH-1", "CREATED", null, "USD");

        Assert.Throws<PaymentStateException>(() => order.AddRefund("REF-1", 10m, "USD", "COMPLETED", "key-1"));
    }
}
