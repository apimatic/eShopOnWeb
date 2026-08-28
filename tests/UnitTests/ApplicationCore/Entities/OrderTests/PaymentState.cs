using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentState
{
    [Fact]
    public void NewOrderAwaitsPaymentAndFulfillment()
    {
        var order = CreateOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Pending, order.FulfillmentStatus);
    }

    [Fact]
    public void AuthorizedOrderBecomesCapturedOnlyAtFulfillment()
    {
        var order = CreateOrder();

        order.MarkAuthorized();
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Pending, order.FulfillmentStatus);

        order.MarkFulfilled();
        Assert.Equal(OrderPaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Fulfilled, order.FulfillmentStatus);
    }

    [Fact]
    public void RefundStateTracksPartialAndFullAmounts()
    {
        var order = CreateOrder();
        order.MarkAuthorized();
        order.MarkFulfilled();

        order.MarkRefunded(5m, 10m);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);

        order.MarkRefunded(10m, 10m);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public void PaymentKeepsAuthorizationHistoryAndRefundReservations()
    {
        var payment = new OrderPayment(1, 10m, "USD", "authorize-key", null);
        var now = DateTimeOffset.UtcNow;
        payment.RecordAuthorization("paypal-order", "COMPLETED", "auth-1", "CREATED", 10m,
            now, now.AddDays(29), false);
        payment.RecordAuthorization("paypal-order", "COMPLETED", "auth-2", "CREATED", 10m,
            now.AddDays(4), now.AddDays(33), true);
        var refund = payment.ReserveRefund("refund-key", "paypal-refund-key", 3m);

        Assert.Equal(2, payment.Authorizations.Count);
        Assert.Equal("auth-2", payment.CurrentAuthorization!.PayPalId);
        Assert.True(payment.CurrentAuthorization.IsReauthorization);
        Assert.Equal(3m, payment.ReservedRefundAmount);
        Assert.Equal(0m, payment.RefundedAmount);

        refund.Complete("refund-1", "COMPLETED");
        Assert.Equal(3m, payment.RefundedAmount);
    }

    private static Order CreateOrder() => new("buyer@example.com",
        new Address("Street", "City", "State", "Country", "12345"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Product", "picture"), 10m, 1)
        });
}
