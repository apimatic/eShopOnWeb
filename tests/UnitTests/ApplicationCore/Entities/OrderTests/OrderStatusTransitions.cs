using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder() =>
        new("buyer@example.com", new Address("1 St", "City", "State", "Country", "12345"), new List<OrderItem>());

    [Fact]
    public void NewOrderIsPlaced()
    {
        Assert.Equal(OrderStatus.Placed, NewOrder().Status);
    }

    [Fact]
    public void DispatchMovesPlacedToDispatched()
    {
        var order = NewOrder();
        order.Dispatch();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesPlacedToCancelled()
    {
        var order = NewOrder();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchedOrderMayStillBeCancelled()
    {
        var order = NewOrder();
        order.Dispatch();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = NewOrder();
        order.Cancel();
        Assert.Throws<InvalidOrderStateException>(() => order.Dispatch());
    }

    [Fact]
    public void OrderCannotBeDispatchedTwice()
    {
        var order = NewOrder();
        order.Dispatch();
        Assert.Throws<InvalidOrderStateException>(() => order.Dispatch());
    }

    [Fact]
    public void OrderCannotBeCancelledTwice()
    {
        var order = NewOrder();
        order.Cancel();
        Assert.Throws<InvalidOrderStateException>(() => order.Cancel());
    }
}
