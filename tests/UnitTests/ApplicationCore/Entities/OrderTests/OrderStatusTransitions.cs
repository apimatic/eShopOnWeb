using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder()
    {
        var address = new Address("street", "city", "state", "country", "zip");
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "item", "uri"), 10m, 2)
        };
        return new Order("buyer-1", address, items);
    }

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        Assert.Equal(OrderStatus.AwaitingPayment, NewOrder().Status);
    }

    [Fact]
    public void AwaitingPayment_To_Authorized_To_Fulfilled()
    {
        var o = NewOrder();
        o.SetAuthorized();
        Assert.Equal(OrderStatus.Authorized, o.Status);
        o.SetFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, o.Status);
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        Assert.Throws<InvalidOperationException>(() => NewOrder().SetFulfilled());
    }

    [Fact]
    public void CanCancelBeforeFulfilmentButNotAfter()
    {
        var o = NewOrder();
        o.SetAuthorized();
        o.SetCancelled();
        Assert.Equal(OrderStatus.Cancelled, o.Status);

        var o2 = NewOrder();
        o2.SetAuthorized();
        o2.SetFulfilled();
        Assert.Throws<InvalidOperationException>(() => o2.SetCancelled());
    }

    [Fact]
    public void RefundReflectsPartialThenFull()
    {
        var o = NewOrder();
        o.SetAuthorized();
        o.SetFulfilled();
        o.SetRefunded(fullyRefunded: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, o.Status);
        o.SetRefunded(fullyRefunded: true);
        Assert.Equal(OrderStatus.Refunded, o.Status);
    }

    [Fact]
    public void CannotRefundAnUnfulfilledOrder()
    {
        var o = NewOrder();
        o.SetAuthorized();
        Assert.Throws<InvalidOperationException>(() => o.SetRefunded(false));
    }
}
