using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private Order NewOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderIsPlaced()
    {
        Assert.Equal(OrderStatus.Placed, NewOrder().Status);
    }

    [Fact]
    public void DispatchMovesToDispatched()
    {
        var order = NewOrder();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void DispatchingTwiceIsRejected()
    {
        var order = NewOrder();
        order.MarkDispatched();
        Assert.Throws<ConflictException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CancelledOrderCannotBeDispatched()
    {
        var order = NewOrder();
        order.MarkCancelled();
        Assert.Throws<ConflictException>(() => order.MarkDispatched());
    }

    [Fact]
    public void DispatchedOrderCanBeCancelled()
    {
        // The key case: cancelling after dispatch is when a queued follow-up must be called off.
        var order = NewOrder();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancellingTwiceIsRejected()
    {
        var order = NewOrder();
        order.MarkCancelled();
        Assert.Throws<ConflictException>(() => order.MarkCancelled());
    }
}
