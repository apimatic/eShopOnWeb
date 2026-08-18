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
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "item", "uri"), 1m, 1)
        };
        return new Order("buyer@example.com", address, items);
    }

    [Fact]
    public void DefaultsToPlaced()
    {
        Assert.Equal(OrderStatus.Placed, NewOrder().Status);
    }

    [Fact]
    public void DispatchMovesToDispatched()
    {
        var order = NewOrder();
        order.Dispatch();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesToCancelled()
    {
        var order = NewOrder();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchedOrderCanStillBeCancelled()
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
        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.Dispatch());
    }

    [Fact]
    public void CancelledOrderCannotBeCancelledAgain()
    {
        var order = NewOrder();
        order.Cancel();
        Assert.Throws<InvalidOrderStatusTransitionException>(() => order.Cancel());
    }
}
