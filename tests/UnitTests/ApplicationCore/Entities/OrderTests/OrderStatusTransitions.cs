using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void MarkDispatchedSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var at = DateTimeOffset.UtcNow;

        order.MarkDispatched(at);

        Assert.Equal(OrderStatus.Dispatched, order.Status);
        Assert.Equal(at, order.DispatchedAt);
    }

    [Fact]
    public void MarkCancelledSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var at = DateTimeOffset.UtcNow;

        order.MarkCancelled(at);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(at, order.CancelledAt);
    }

    [Fact]
    public void CannotDispatchACancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled(DateTimeOffset.UtcNow);

        Assert.Throws<OrderStateException>(() => order.MarkDispatched(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched(DateTimeOffset.UtcNow);

        Assert.Throws<OrderStateException>(() => order.MarkDispatched(DateTimeOffset.UtcNow));
    }
}
