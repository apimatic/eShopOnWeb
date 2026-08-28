using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderFulfillment
{
    [Fact]
    public void DispatchesOnlyOnceAndCannotDispatchAfterCancellation()
    {
        var order = CreateOrder();
        var now = DateTimeOffset.UtcNow;

        Assert.True(order.Dispatch(now));
        Assert.Equal(OrderFulfillmentStatus.Dispatched, order.FulfillmentStatus);
        Assert.Equal(now, order.DispatchedAt);
        Assert.False(order.Dispatch(now.AddMinutes(1)));

        Assert.True(order.Cancel(now.AddMinutes(2)));
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.False(order.Dispatch(now.AddMinutes(3)));
    }

    [Fact]
    public void CancellationIsIdempotent()
    {
        var order = CreateOrder();
        var now = DateTimeOffset.UtcNow;

        Assert.True(order.Cancel(now));
        Assert.False(order.Cancel(now.AddMinutes(1)));
        Assert.Equal(now, order.CancelledAt);
    }

    private static Order CreateOrder() => new("shopper@example.com",
        new Address("1 Main St", "Toronto", "ON", "Canada", "M5V 1A1"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Item", "item.png"), 10m, 1)
        });
}
