using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    [Fact]
    public void StartsAwaitingPaymentWithNoPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AttachPaymentMovesToPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment(order.Id, order.Total(), "USD");

        order.AttachPayment(payment);

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Same(payment, order.Payment);
    }

    [Fact]
    public void AttachPaymentTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(new Payment(order.Id, order.Total(), "USD"));

        Assert.Throws<InvalidOperationException>(() => order.AttachPayment(new Payment(order.Id, order.Total(), "USD")));
    }

    [Fact]
    public void MarkFulfilledRequiresPaymentAuthorizedFirst()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void MarkFulfilledMovesToFulfilledFromPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(new Payment(order.Id, order.Total(), "USD"));

        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void MarkCancelledRequiresPaymentAuthorizedFirst()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CannotCancelAFulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(new Payment(order.Id, order.Total(), "USD"));
        order.MarkFulfilled();

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }
}
