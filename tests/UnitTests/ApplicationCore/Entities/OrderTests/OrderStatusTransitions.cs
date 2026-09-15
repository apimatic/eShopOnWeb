using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitions
{
    private static Order NewOrder() =>
        new("buyer@example.com", new Address("1 St", "City", "ST", "US", "12345"), new List<OrderItem>());

    [Fact]
    public void New_order_awaits_payment_and_has_a_unique_idempotency_token()
    {
        var a = NewOrder();
        var b = NewOrder();

        Assert.Equal(OrderStatus.AwaitingPayment, a.Status);
        Assert.NotEqual(Guid.Empty, a.IdempotencyToken);
        Assert.NotEqual(a.IdempotencyToken, b.IdempotencyToken);
    }

    [Fact]
    public void Authorize_then_fulfil_is_allowed()
    {
        var order = NewOrder();

        order.MarkPaymentAuthorized();
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);

        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void Cannot_fulfil_before_authorization()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void Cannot_cancel_after_fulfilment()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void Can_cancel_while_awaiting_payment_or_authorized()
    {
        var awaiting = NewOrder();
        awaiting.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, awaiting.Status);

        var authorized = NewOrder();
        authorized.MarkPaymentAuthorized();
        authorized.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, authorized.Status);
    }

    [Fact]
    public void Refund_requires_a_fulfilled_order()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();

        Assert.Throws<InvalidOperationException>(() => order.MarkRefunded(fullyRefunded: true));

        order.MarkFulfilled();
        order.MarkRefunded(fullyRefunded: false);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        order.MarkRefunded(fullyRefunded: true);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
