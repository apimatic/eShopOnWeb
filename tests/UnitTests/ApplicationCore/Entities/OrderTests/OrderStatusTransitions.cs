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
    public void MarkDispatchedSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void MarkCancelledSetsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CancellingTwiceIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
