using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderIsPending()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void DispatchMovesPendingToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesPendingToCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchAfterCancelThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void DispatchTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CancelAfterDispatchIsAllowed()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
