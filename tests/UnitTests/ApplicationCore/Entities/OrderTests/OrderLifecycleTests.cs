using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderLifecycleTests
{
    private static Order NewOrder()
    {
        var address = new Address("1 St", "City", "ST", "US", "00000");
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "uri"), 10m, 2);
        return new Order("buyer@example.com", address, new List<OrderItem> { item });
    }

    [Fact]
    public void NewOrder_AwaitsPayment_WithNoPayment()
    {
        var order = NewOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
        Assert.Equal(20m, order.Total());
    }

    [Fact]
    public void StartPayment_IsIdempotent()
    {
        var order = NewOrder();
        var p1 = order.StartPayment("USD");
        var p2 = order.StartPayment("USD");
        Assert.Same(p1, p2);
        Assert.Equal(order.Total(), p1.Amount);
    }

    [Fact]
    public void ApplyRefundOutcome_TransitionsToPartiallyThenFullyRefunded()
    {
        var order = NewOrder();
        var p = order.StartPayment("USD");
        p.SetAuthorization("O", "A", "CREATED", null);
        p.SetCapture("CAP", "COMPLETED", 20m, 0.9m, 19.1m);
        order.MarkFulfilled();

        p.AddRefund("k1", 5m, "R1", "COMPLETED");
        order.ApplyRefundOutcome();
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        p.AddRefund("k2", 15m, "R2", "COMPLETED");
        order.ApplyRefundOutcome();
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
