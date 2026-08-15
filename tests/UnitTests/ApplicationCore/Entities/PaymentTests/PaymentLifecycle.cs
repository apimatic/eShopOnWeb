using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    private static Order CapturedOrder(decimal total, out Payment payment)
    {
        var address = new Address("1 St", "City", "ST", "US", "00000");
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Item", "pic.png"), total, 1)
        };
        var order = new Order("buyer@example.com", address, items);

        payment = new Payment(order.Id, total, "USD", "ESHOP-recon");
        payment.RecordAuthorization("PO-1", "AUTH-1", "CREATED", null, "VISA", "1111", null);
        order.SetAuthorized(payment);
        payment.RecordCapture("CAP-1", "COMPLETED", total, 1.00m, total - 1.00m);
        order.SetFulfilled();
        return order;
    }

    [Fact]
    public void RemainingRefundable_TracksPartialRefunds()
    {
        var order = CapturedOrder(50m, out var payment);

        Assert.Equal(50m, payment.RemainingRefundable());

        payment.AddRefund(new PaymentRefund("k1", "R1", 20m, "USD", "COMPLETED"));
        Assert.Equal(20m, payment.TotalRefunded());
        Assert.Equal(30m, payment.RemainingRefundable());

        payment.AddRefund(new PaymentRefund("k2", "R2", 30m, "USD", "COMPLETED"));
        Assert.Equal(50m, payment.TotalRefunded());
        Assert.Equal(0m, payment.RemainingRefundable());
    }

    [Fact]
    public void FindRefundByKey_ReturnsExistingRefund()
    {
        var order = CapturedOrder(50m, out var payment);
        payment.AddRefund(new PaymentRefund("key-A", "R1", 10m, "USD", "COMPLETED"));

        Assert.NotNull(payment.FindRefundByKey("key-A"));
        Assert.Null(payment.FindRefundByKey("key-B"));
    }

    [Fact]
    public void SetRefundState_PartialThenFull()
    {
        var order = CapturedOrder(40m, out var payment);

        payment.AddRefund(new PaymentRefund("k1", "R1", 15m, "USD", "COMPLETED"));
        order.SetRefundState();
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        payment.AddRefund(new PaymentRefund("k2", "R2", 25m, "USD", "COMPLETED"));
        order.SetRefundState();
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void OrderStatus_TransitionsThroughAuthorizeAndCancel()
    {
        var address = new Address("1 St", "City", "ST", "US", "00000");
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), 10m, 1) };
        var order = new Order("buyer@example.com", address, items);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);

        var payment = new Payment(order.Id, 10m, "USD", "recon");
        payment.RecordAuthorization("PO", "AUTH", "CREATED", null, "VISA", "1111", null);
        order.SetAuthorized(payment);
        Assert.Equal(OrderStatus.Authorized, order.Status);

        payment.RecordVoid();
        order.SetCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
