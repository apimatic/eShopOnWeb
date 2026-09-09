using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    private static Order NewOrder() => new OrderBuilder().WithDefaultValues();

    private static Payment NewPayment() =>
        new("PPORDER1", "AUTH1", "CREATED", "USD", 100m, DateTimeOffset.UtcNow.AddDays(29));

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        Assert.Equal(OrderStatus.AwaitingPayment, NewOrder().Status);
    }

    [Fact]
    public void AuthorizeMovesToPaymentAuthorized()
    {
        var order = NewOrder();
        order.SetAuthorized(NewPayment());

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void CannotFulfilBeforePayment()
    {
        Assert.Throws<InvalidOperationException>(() => NewOrder().SetFulfilled());
    }

    [Fact]
    public void FulfilAfterAuthorization()
    {
        var order = NewOrder();
        order.SetAuthorized(NewPayment());
        order.SetFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CancelOnlyBeforeFulfilment()
    {
        var order = NewOrder();
        order.SetAuthorized(NewPayment());
        order.SetFulfilled();

        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void CancelReleasesAuthorizedOrder()
    {
        var order = NewOrder();
        order.SetAuthorized(NewPayment());
        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
