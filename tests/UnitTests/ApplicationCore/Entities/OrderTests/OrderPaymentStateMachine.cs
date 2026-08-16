using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

/// <summary>
/// Covers the additive payment/fulfilment state machine on the Order aggregate — the invariants that
/// keep a double-click or a bad operator action from moving money incorrectly.
/// </summary>
public class OrderPaymentStateMachine
{
    private static OrderPayment AuthorizedPayment(decimal amount = 29m)
    {
        var payment = new OrderPayment("PayPal", "USD", amount, "ESHOP-1-ref", "VISA ending 1111");
        payment.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        return payment;
    }

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AuthorizeMovesOrderToAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(AuthorizedPayment());

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
        Assert.Equal(PaymentStatus.Authorized, order.Payment!.Status);
    }

    [Fact]
    public void AuthorizeTwiceIsRejected()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(AuthorizedPayment());

        Assert.Throws<OrderPaymentException>(() => order.AuthorizePayment(AuthorizedPayment()));
    }

    [Fact]
    public void FulfilRequiresAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<OrderPaymentException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void FulfilledOrderCannotBeCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(AuthorizedPayment());
        order.MarkFulfilled();

        Assert.Throws<OrderPaymentException>(() => order.Cancel());
    }

    [Fact]
    public void CancelReleasesAnAuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AuthorizePayment(AuthorizedPayment());
        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
