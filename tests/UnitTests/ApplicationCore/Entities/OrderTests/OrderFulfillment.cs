using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderFulfillment
{
    [Fact]
    public void StartsAsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderFulfillmentStatus.Placed, order.Status);
    }

    [Fact]
    public void DispatchMovesFromPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderFulfillmentStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesFromPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CancelAfterDispatchStopsFulfillment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<InvalidOrderOperationException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<InvalidOrderOperationException>(() => order.MarkDispatched());
    }
}
