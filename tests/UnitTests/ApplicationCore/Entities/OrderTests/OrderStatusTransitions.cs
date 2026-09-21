using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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
    public void MarkDispatchedFromPlacedChangesState()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var changed = order.MarkDispatched();

        Assert.True(changed);
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkDispatchedIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        var changedAgain = order.MarkDispatched();

        Assert.False(changedAgain);
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkCancelledFromPlacedChangesState()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var changed = order.MarkCancelled();

        Assert.True(changed);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledFromDispatchedChangesState()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        var changed = order.MarkCancelled();

        Assert.True(changed);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        var changedAgain = order.MarkCancelled();

        Assert.False(changedAgain);
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        var dispatched = order.MarkDispatched();

        Assert.False(dispatched);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
