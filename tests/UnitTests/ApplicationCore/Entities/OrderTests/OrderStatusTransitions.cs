using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder()
    {
        var builder = new OrderBuilder();
        return builder.WithItems(new List<OrderItem> { new(builder.TestCatalogItemOrdered, 10m, 1) });
    }

    [Fact]
    public void NewOrder_StartsAwaitingPayment()
    {
        Assert.Equal(OrderStatus.AwaitingPayment, NewOrder().Status);
    }

    [Fact]
    public void HappyPath_AwaitingPayment_To_Fulfilled()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CannotFulfil_BeforeAuthorization()
    {
        Assert.Throws<PaymentDomainException>(() => NewOrder().MarkFulfilled());
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Throws<PaymentDomainException>(() => order.MarkAuthorized());
    }

    [Fact]
    public void CannotCancel_AfterFulfilment()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkFulfilled();
        Assert.Throws<PaymentDomainException>(() => order.MarkCancelled());
    }

    [Fact]
    public void Refund_PartialThenFull_UpdatesStatus()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkFulfilled();

        order.MarkRefunded(fullyRefunded: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        order.MarkRefunded(fullyRefunded: true);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
