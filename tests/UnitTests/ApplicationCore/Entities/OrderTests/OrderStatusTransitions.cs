using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void DispatchThenCancel()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotDispatchCancelledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<OrderTransitionException>(() => order.MarkDispatched());
    }

    [Fact]
    public void CannotDispatchTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<OrderTransitionException>(() => order.MarkDispatched());
    }
}
