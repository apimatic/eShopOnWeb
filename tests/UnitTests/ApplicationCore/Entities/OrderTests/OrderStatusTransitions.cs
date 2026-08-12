using System;
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
    public void DispatchMovesPlacedToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Dispatch();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesPlacedToCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchedOrderCanStillBeCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Dispatch();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Cancel();
        Assert.Throws<InvalidOperationException>(() => order.Dispatch());
    }

    [Fact]
    public void CancelledOrderCannotBeCancelledAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Cancel();
        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }
}
