using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderStatusTransitionTests
{
    private static Order NewOrder()
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), 25m, 2),
        };
        return new Order("buyer-1", new Address("s", "c", "st", "US", "00000"), items);
    }

    private static OrderPayment NewPayment() => new("PPO", "AUTH", "CREATED", 50m, "USD", null);

    [Fact]
    public void NewOrder_IsAwaitingPayment()
    {
        Assert.Equal(OrderStatus.AwaitingPayment, NewOrder().Status);
        Assert.Equal(50m, NewOrder().Total());
    }

    [Fact]
    public void MarkAuthorized_ThenFulfilled()
    {
        var order = NewOrder();
        order.MarkAuthorized(NewPayment());
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.Payment!.RecordCapture("CAP", "COMPLETED", 50m, 2m, 48m);
        order.MarkFulfilled();
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public void MarkAuthorized_Twice_Throws()
    {
        var order = NewOrder();
        order.MarkAuthorized(NewPayment());
        Assert.Throws<PaymentOperationException>(() => order.MarkAuthorized(NewPayment()));
    }

    [Fact]
    public void Cancel_BeforeFulfilment_VoidsAndCancels()
    {
        var order = NewOrder();
        order.MarkAuthorized(NewPayment());
        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void Cancel_AfterFulfilment_Throws()
    {
        var order = NewOrder();
        order.MarkAuthorized(NewPayment());
        order.Payment!.RecordCapture("CAP", "COMPLETED", 50m, 2m, 48m);
        order.MarkFulfilled();

        Assert.Throws<PaymentOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void ReflectRefundState_PartialThenFull()
    {
        var order = NewOrder();
        order.MarkAuthorized(NewPayment());
        order.Payment!.RecordCapture("CAP", "COMPLETED", 50m, 2m, 48m);
        order.MarkFulfilled();

        order.Payment.AddRefund("R1", 20m, "COMPLETED", "k1");
        order.ReflectRefundState();
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        order.Payment.AddRefund("R2", 30m, "COMPLETED", "k2");
        order.ReflectRefundState();
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
