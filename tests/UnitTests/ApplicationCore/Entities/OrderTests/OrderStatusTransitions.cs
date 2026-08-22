using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderStartsSubmitted()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.Submitted, order.Status);
    }

    [Fact]
    public void MarkDispatchedTransitionsFromSubmitted()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkCancelledTransitionsFromSubmitted()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchAfterCancel()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }
}
