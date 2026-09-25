using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStatusTransitions
{
    private static Order NewOrder() =>
        new("buyer@x.com", new Address("s", "c", "st", "co", "z"), new List<OrderItem>());

    [Fact]
    public void New_order_awaits_payment_and_has_a_payment_reference()
    {
        var order = NewOrder();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.NotEqual(Guid.Empty, order.PaymentReference);
    }

    [Fact]
    public void Authorize_then_pay_then_refund_walks_the_state_machine()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        order.MarkPaid();
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        order.MarkRefunded(fullyRefunded: false);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        order.MarkRefunded(fullyRefunded: true);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public void MarkAuthorized_is_idempotent()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkAuthorized();
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
    }

    [Fact]
    public void Cannot_pay_an_order_that_was_not_authorized()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOperationException>(() => order.MarkPaid());
    }

    [Fact]
    public void Cannot_cancel_after_capture()
    {
        var order = NewOrder();
        order.MarkAuthorized();
        order.MarkPaid();
        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }
}
