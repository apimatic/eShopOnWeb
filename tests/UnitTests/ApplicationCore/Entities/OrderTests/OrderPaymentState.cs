using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Order NewOrder()
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), 10m, 2)
        };
        return new Order("buyer@example.com", new Address("s", "c", "st", "co", "z"), items);
    }

    private static Payment NewPayment() => new("po-1", "auth-1", "CREATED", 20m, "USD");

    [Fact]
    public void NewOrder_IsAwaitingPayment_WithUniqueReference()
    {
        var a = NewOrder();
        var b = NewOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, a.Status);
        Assert.False(string.IsNullOrEmpty(a.PaymentReference));
        Assert.NotEqual(a.PaymentReference, b.PaymentReference);
    }

    [Fact]
    public void AttachAuthorization_MovesToAuthorized()
    {
        var order = NewOrder();
        order.AttachAuthorization(NewPayment());
        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        var order = NewOrder();
        Assert.Throws<PaymentStateException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = NewOrder();
        order.AttachAuthorization(NewPayment());
        order.MarkFulfilled();
        Assert.Throws<PaymentStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void RefundState_PartialThenFull()
    {
        var order = NewOrder();
        var payment = NewPayment();
        order.AttachAuthorization(payment);
        payment.RecordCapture("cap-1", "COMPLETED", 20m, 1m, 19m);
        order.MarkFulfilled();

        payment.AddRefund(new PaymentRefund("r1", 5m, "COMPLETED", "k1"));
        order.ApplyRefundState();
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(15m, payment.RemainingCapturedAmount());

        payment.AddRefund(new PaymentRefund("r2", 15m, "COMPLETED", "k2"));
        order.ApplyRefundState();
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, payment.RemainingCapturedAmount());
    }

    [Fact]
    public void RefundKeyLookup_FindsRecordedRefund()
    {
        var payment = NewPayment();
        payment.RecordCapture("cap-1", "COMPLETED", 20m, 1m, 19m);
        payment.AddRefund(new PaymentRefund("r1", 5m, "COMPLETED", "my-key"));

        Assert.NotNull(payment.FindRefundByKey("my-key"));
        Assert.Null(payment.FindRefundByKey("other-key"));
    }
}
