using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderStartsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void MarkDispatchedChangesStatusOnce()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.True(order.MarkDispatched());
        Assert.Equal(OrderStatus.Dispatched, order.Status);
        Assert.False(order.MarkDispatched());
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.True(order.MarkCancelled());
        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void DispatchedOrderCanBeCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.True(order.MarkCancelled());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
