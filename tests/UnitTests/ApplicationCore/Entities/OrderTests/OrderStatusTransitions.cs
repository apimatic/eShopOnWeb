using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private Order NewOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        Assert.Equal(OrderStatus.AwaitingPayment, NewOrder().Status);
    }

    [Fact]
    public void AuthorizeThenFulfilFollowsTheHappyPath()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CannotFulfilBeforeAuthorizing()
    {
        var order = NewOrder();
        Assert.Throws<PaymentApiException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Throws<PaymentApiException>(() => order.MarkAuthorized());
    }

    [Fact]
    public void CanCancelBeforeFulfilment()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkFulfilled();
        var ex = Assert.Throws<PaymentApiException>(() => order.MarkCancelled());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void RefundStateRequiresFulfilment()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Throws<PaymentApiException>(() => order.MarkRefundState(fullyRefunded: false));
    }

    [Fact]
    public void RefundStateReflectsPartialAndFull()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkFulfilled();
        order.MarkRefundState(fullyRefunded: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        order.MarkRefundState(fullyRefunded: true);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
