using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Payment NewPayment() => new(
        orderId: 1, currency: "USD", payPalCustomId: "ESHOP-1-x", authorizedAmount: 10m,
        payPalOrderId: "PPO", authorizationId: "AUTH", authorizationStatus: Payment.AuthCreated,
        authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(29), authorizationRequestId: "req",
        cardBrand: "VISA", cardLast4: "1111", savedCardId: null);

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AuthorizingMovesToPaymentAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorizedPayment(NewPayment());

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorizedPayment(NewPayment());
        Assert.Throws<InvalidOperationException>(() => order.SetAuthorizedPayment(NewPayment()));
    }

    [Fact]
    public void FulfilRequiresAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorizedPayment(NewPayment());
        order.MarkFulfilled();
        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void CanCancelWhileAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorizedPayment(NewPayment());
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
