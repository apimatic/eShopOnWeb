using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderStartsPlaced()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void DispatchMarksDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CannotDispatchCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<InvalidOperationException>(() => order.MarkDispatched());
    }
}
