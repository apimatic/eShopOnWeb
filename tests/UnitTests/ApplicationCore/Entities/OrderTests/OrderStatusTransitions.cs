using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void FollowsTheHappyPath()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkPaymentAuthorized();
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);

        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);

        order.MarkRefunded(inFull: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        order.MarkRefunded(inFull: true);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void CannotFulfilBeforePayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<PaymentConflictException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CannotPayTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        Assert.Throws<PaymentConflictException>(() => order.MarkPaymentAuthorized());
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<PaymentConflictException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CancelledOrderCanNoLongerBePaidOrFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<PaymentConflictException>(() => order.MarkPaymentAuthorized());
        Assert.Throws<PaymentConflictException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void UnrenewableAuthorizationReturnsOrderToPendingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        order.MarkPaymentRequired();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }
}
