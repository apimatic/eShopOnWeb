using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderLifecycleTests
{
    private static Order NewOrder() =>
        new("buyer@test.com",
            new Address("1 St", "City", "State", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "uri"), 10m, 1) });

    [Fact]
    public void NewOrderStartsSubmitted()
    {
        Assert.Equal(OrderStatus.Submitted, NewOrder().Status);
    }

    [Fact]
    public void DispatchMovesSubmittedToDispatched()
    {
        var order = NewOrder();
        order.MarkAsDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CannotDispatchACancelledOrder()
    {
        var order = NewOrder();
        order.MarkAsCancelled();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkAsDispatched());
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = NewOrder();
        order.MarkAsDispatched();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkAsDispatched());
    }

    [Fact]
    public void CanCancelADispatchedOrder()
    {
        var order = NewOrder();
        order.MarkAsDispatched();
        order.MarkAsCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelTwice()
    {
        var order = NewOrder();
        order.MarkAsCancelled();
        Assert.Throws<InvalidOrderStateException>(() => order.MarkAsCancelled());
    }
}
