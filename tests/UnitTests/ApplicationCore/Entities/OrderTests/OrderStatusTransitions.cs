using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private readonly OrderBuilder _builder = new();

    [Fact]
    public void NewOrderStartsAsPlaced()
    {
        var order = _builder.WithDefaultValues();
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void DispatchMovesOrderToDispatched()
    {
        var order = _builder.WithDefaultValues();
        order.MarkDispatched();
        Assert.Equal(OrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void CancelAfterDispatchIsAllowed()
    {
        var order = _builder.WithDefaultValues();
        order.MarkDispatched();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void DispatchOfCancelledOrderThrows()
    {
        var order = _builder.WithDefaultValues();
        order.MarkCancelled();
        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }

    [Fact]
    public void DoubleDispatchThrows()
    {
        var order = _builder.WithDefaultValues();
        order.MarkDispatched();
        Assert.Throws<OrderStateException>(() => order.MarkDispatched());
    }
}
