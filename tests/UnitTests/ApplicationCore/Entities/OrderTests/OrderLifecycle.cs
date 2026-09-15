using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderLifecycle
{
    [Fact]
    public void DispatchIsIdempotentAndCancellationIsFinal()
    {
        var order = new Order("shopper", new AddressBuilder().Build(), []);

        Assert.True(order.MarkDispatched(DateTimeOffset.UtcNow));
        Assert.False(order.MarkDispatched(DateTimeOffset.UtcNow));
        Assert.Equal(OrderLifecycleStatus.Dispatched, order.Status);

        Assert.True(order.MarkCancelled(DateTimeOffset.UtcNow));
        Assert.False(order.MarkCancelled(DateTimeOffset.UtcNow));
        Assert.Equal(OrderLifecycleStatus.Cancelled, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.MarkDispatched(DateTimeOffset.UtcNow));
    }
}
