using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStateMachine
{
    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void BeginPaymentCreatesPaymentWithOrderTotal()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var payment = order.BeginPayment("USD");

        Assert.Equal(order.Total(), payment.AuthorizedAmount);
        Assert.Equal("USD", payment.CurrencyCode);
        Assert.Equal(OrderPaymentStatus.AwaitingAuthorization, payment.Status);
    }

    [Fact]
    public void BeginPaymentIsIdempotentWhileAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        var first = order.BeginPayment("USD");
        var second = order.BeginPayment("USD");

        Assert.Same(first, second);
    }

    [Fact]
    public void MarkFulfilledThrowsWhenNotAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<OrderPaymentStateException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void MarkFulfilledSucceedsAfterAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginPayment("USD");
        order.MarkPaymentAuthorized();

        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void MarkCancelledThrowsAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginPayment("USD");
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<OrderPaymentStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void MarkCancelledSucceedsBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkRefundedThrowsBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<OrderPaymentStateException>(() => order.MarkRefunded(true));
    }
}
