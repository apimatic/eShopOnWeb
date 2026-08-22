using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderStartsAsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderFulfillmentStatus.Placed, order.Status);
    }

    [Fact]
    public void DispatchMovesPlacedOrderToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderFulfillmentStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesOrderToCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchACancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Throws<System.InvalidOperationException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();

        Assert.Throws<System.InvalidOperationException>(() => order.MarkDispatched());
    }
}
