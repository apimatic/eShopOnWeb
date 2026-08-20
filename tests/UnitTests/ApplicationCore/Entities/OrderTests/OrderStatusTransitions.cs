using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderStartsAsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void MarkDispatchedSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CannotDispatchACancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<InvalidOperationException>(order.MarkDispatched);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CanCancelADispatchedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
