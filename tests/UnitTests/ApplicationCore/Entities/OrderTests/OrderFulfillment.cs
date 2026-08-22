using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderFulfillment
{
    [Fact]
    public void StartsAsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderFulfillmentStatus.Placed, order.FulfillmentStatus);
    }

    [Fact]
    public void DispatchMovesToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderFulfillmentStatus.Dispatched, order.FulfillmentStatus);
    }

    [Fact]
    public void CancelMovesToCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.FulfillmentStatus);
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
