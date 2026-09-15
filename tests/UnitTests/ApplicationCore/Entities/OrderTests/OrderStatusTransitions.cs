using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderIsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void MarkDispatchedMovesPlacedToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkDispatchedTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void MarkDispatchedAfterCancelThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void MarkCancelledFromPlacedSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledFromDispatchedSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }
}
