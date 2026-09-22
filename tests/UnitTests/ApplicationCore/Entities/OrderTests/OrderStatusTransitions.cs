using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder()
    {
        var address = new Address("street", "city", "state", "country", "zip");
        return new Order("buyer@example.com", address, new List<OrderItem>());
    }

    [Fact]
    public void NewOrderStartsPlaced()
    {
        Assert.Equal(OrderStatus.Placed, NewOrder().Status);
    }

    [Fact]
    public void DispatchChangesStateOnceThenNoOps()
    {
        var order = NewOrder();

        Assert.True(order.Dispatch());               // state changed
        Assert.Equal(OrderStatus.Dispatched, order.Status);
        Assert.False(order.Dispatch());              // repeat is a no-op — gates re-notification off
    }

    [Fact]
    public void CancelChangesStateOnceThenNoOps()
    {
        var order = NewOrder();

        Assert.True(order.Cancel());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.False(order.Cancel());
    }

    [Fact]
    public void DispatchedOrderCanBeCancelled()
    {
        var order = NewOrder();
        order.Dispatch();

        Assert.True(order.Cancel());                 // must allow cancelling after dispatch (to stop follow-up)
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = NewOrder();
        order.Cancel();

        Assert.Throws<InvalidOrderStateException>(() => order.Dispatch());
    }
}
