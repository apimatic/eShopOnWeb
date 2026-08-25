using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    private static Payment NewPayment(int orderId) =>
        new(orderId, "USD", 42m, "PP-ORDER-1", "AUTH-1", "CREATED", "req-1", null);

    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AttachPaymentMovesToPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.AttachPayment(NewPayment(1));

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void AttachPaymentTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(NewPayment(1));

        Assert.Throws<InvalidOperationException>(() => order.AttachPayment(NewPayment(1)));
    }

    [Fact]
    public void MarkFulfilledRequiresAuthorizedPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void MarkFulfilledSucceedsAfterAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(NewPayment(1));

        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void MarkCancelledRequiresAuthorizedPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CannotCancelAfterFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(NewPayment(1));
        order.MarkFulfilled();

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }
}
