using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStateTransitions
{
    private static Payment NewPayment() =>
        new("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), 3.69m, "USD", "INV-1");

    [Fact]
    public void NewOrderIsAwaitingPaymentWithNoPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AuthorizingMovesToPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(NewPayment());

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(NewPayment());
        Assert.Throws<InvalidOperationException>(() => order.AuthorizePayment(NewPayment()));
    }

    [Fact]
    public void FulfilRequiresAnAuthorizedPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CanFulfilAfterAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(NewPayment());
        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CannotCancelAFulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(NewPayment());
        order.MarkFulfilled();
        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CanCancelBeforeAndAfterAuthorization()
    {
        var awaiting = new OrderBuilder().WithDefaultValues();
        awaiting.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, awaiting.Status);

        var authorized = new OrderBuilder().WithDefaultValues();
        authorized.AuthorizePayment(NewPayment());
        authorized.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, authorized.Status);
    }
}
