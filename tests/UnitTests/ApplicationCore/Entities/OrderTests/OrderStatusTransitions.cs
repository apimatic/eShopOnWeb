using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder() =>
        new("buyer", new Address("1 St", "City", "ST", "Country", "00000"), new List<OrderItem>());

    [Fact]
    public void StartsPlaced()
    {
        Assert.Equal(OrderStatus.Placed, NewOrder().Status);
    }

    [Fact]
    public void CanBeDispatchedFromPlaced()
    {
        var order = NewOrder();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CanBeCancelledFromPlaced()
    {
        var order = NewOrder();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CanBeCancelledAfterDispatch()
    {
        var order = NewOrder();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotBeDispatchedAfterCancellation()
    {
        var order = NewOrder();
        order.MarkCancelled();
        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.MarkDispatched());
    }

    [Fact]
    public void DispatchAndCancelAreIdempotent()
    {
        var order = NewOrder();
        order.MarkDispatched();
        order.MarkDispatched(); // no throw
        Assert.Equal(OrderStatus.Dispatched, order.Status);

        order.MarkCancelled();
        order.MarkCancelled(); // no throw
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
