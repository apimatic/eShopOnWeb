using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatus
{
    [Fact]
    public void NewOrderStartsAsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderFulfillmentStatus.Placed, order.Status);
    }

    [Fact]
    public void MarkDispatchedTransitionsFromPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderFulfillmentStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkDispatchedTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<DuplicateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<OrderTransitionException>(() => order.MarkDispatched());
    }

    [Fact]
    public void MarkCancelledTransitionsFromPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CanCancelAfterDispatch()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<DuplicateException>(() => order.MarkCancelled());
    }
}
