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
    public void PlacedOrderCanBeDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void PlacedOrderCanBeCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchedOrderCanBeCancelled_SoItsFollowUpCanBeCalledOff()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CancelledOrderCannotBeCancelledAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.MarkCancelled());
    }

    [Fact]
    public void DispatchedOrderCannotBeDispatchedAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.MarkDispatched());
    }
}
