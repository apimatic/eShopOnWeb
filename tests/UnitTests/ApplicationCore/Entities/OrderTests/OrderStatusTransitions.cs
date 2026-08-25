using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void MarkPaymentAuthorizedFromAwaitingPaymentSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkPaymentAuthorized();

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public void MarkFulfilledFromPaymentAuthorizedSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void MarkCancelledFromAwaitingPaymentSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledFromPaymentAuthorizedSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkFulfilledWithoutAuthorizationThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void MarkCancelledAfterFulfilledThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void MarkPaymentAuthorizedTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkPaymentAuthorized());
    }
}
