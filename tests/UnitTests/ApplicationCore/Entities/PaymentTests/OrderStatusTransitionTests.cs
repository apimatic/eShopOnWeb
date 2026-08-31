using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderStatusTransitionTests
{
    private static Order NewOrder()
    {
        var address = new Address("1 Main St", "Redmond", "WA", "US", "98052");
        var item = new OrderItem(new CatalogItemOrdered(1, "Test item", "pic.png"), 10m, 1);
        return new Order("buyer@example.com", address, new List<OrderItem> { item });
    }

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        Assert.Equal(OrderStatus.PendingPayment, NewOrder().Status);
    }

    [Fact]
    public void HappyPathTransitions()
    {
        var order = NewOrder();

        order.MarkPaymentAuthorized();
        Assert.Equal(OrderStatus.AwaitingFulfilment, order.Status);

        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);

        order.MarkRefunded(fullyRefunded: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        order.MarkRefunded(fullyRefunded: true);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void CannotFulfilUnpaidOrder()
    {
        Assert.Throws<OrderStateException>(() => NewOrder().MarkFulfilled());
    }

    [Fact]
    public void CannotPayTwice()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();

        Assert.Throws<OrderStateException>(() => order.MarkPaymentAuthorized());
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<OrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CanCancelBeforeFulfilment()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotRefundUnfulfilledOrder()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();

        Assert.Throws<OrderStateException>(() => order.MarkRefunded(true));
    }
}
