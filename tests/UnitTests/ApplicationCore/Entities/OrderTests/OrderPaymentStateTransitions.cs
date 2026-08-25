using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStateTransitions
{
    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void MarkPaymentAuthorizedMovesToPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkPaymentAuthorized();

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public void MarkPaymentAuthorizedTwiceThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        Assert.Throws<OrderStateException>(() => order.MarkPaymentAuthorized());
    }

    [Fact]
    public void MarkFulfilledBeforeAuthorizationThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<OrderStateException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void MarkFulfilledAfterAuthorizationSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void CancelAfterFulfilmentThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<OrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CancelBeforePaymentSucceeds()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void RefundBeforeFulfilmentThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();

        Assert.Throws<OrderStateException>(() => order.MarkRefunded(isPartial: false));
    }

    [Fact]
    public void PartialRefundAfterFulfilmentSetsPartiallyRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        order.MarkRefunded(isPartial: true);

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }
}
