using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderIsSubmitted()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.Submitted, order.Status);
    }

    [Fact]
    public void MarkDispatchedSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkCancelledFromSubmittedSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledAfterDispatchSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchACancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotCancelTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }
}
