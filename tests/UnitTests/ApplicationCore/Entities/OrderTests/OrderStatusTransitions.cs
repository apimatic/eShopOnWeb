using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void AuthorizeThenFulfilFollowsTheHappyPath()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CannotFulfilBeforePayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<InvalidPaymentStateException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        Assert.Throws<InvalidPaymentStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        Assert.Throws<InvalidPaymentStateException>(() => order.MarkPaymentAuthorized());
    }
}
