using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void DispatchMovesPendingOrderToDispatched()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkDispatched();

        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelMovesDispatchedOrderToCancelled()
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

        Assert.Throws<InvalidOrderTransitionException>(order.MarkDispatched);
    }
}
