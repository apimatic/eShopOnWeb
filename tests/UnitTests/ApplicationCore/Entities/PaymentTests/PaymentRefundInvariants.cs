using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundInvariants
{
    private static Order AuthorizedCapturedOrder(decimal total, out Payment payment)
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), total, 1)
        };
        var order = new Order("buyer@test", new Address("s", "c", "st", "co", "z"), items);
        payment = order.StartPayment("USD");
        payment.SetAuthorization("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA ****1111");
        order.MarkAuthorized();
        payment.SetCapture("CAP-1", "COMPLETED", total, 1.24m, total - 1.24m);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public void RefundableRemaining_StartsAtCapturedAmount()
    {
        AuthorizedCapturedOrder(29.00m, out var payment);
        Assert.Equal(29.00m, payment.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_ReducesRemaining_AndSumsTotalRefunded()
    {
        var order = AuthorizedCapturedOrder(29.00m, out var payment);

        payment.AddRefund("key-a", 10.00m, "USD");
        payment.AddRefund("key-b", 5.00m, "USD");
        order.ApplyRefundOutcome();

        Assert.Equal(15.00m, payment.TotalRefunded());
        Assert.Equal(14.00m, payment.RefundableRemaining());
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public void AddRefund_BeyondCaptured_Throws()
    {
        AuthorizedCapturedOrder(29.00m, out var payment);
        payment.AddRefund("key-a", 20.00m, "USD");

        // Only 9.00 remains; a further 10.00 must be rejected.
        var ex = Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-b", 10.00m, "USD"));
        Assert.Contains("exceeds", ex.Message);
    }

    [Fact]
    public void FullRefund_MarksOrderRefunded()
    {
        var order = AuthorizedCapturedOrder(29.00m, out var payment);

        payment.AddRefund("key-a", 29.00m, "USD");
        order.ApplyRefundOutcome();

        Assert.Equal(0m, payment.RefundableRemaining());
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExisting()
    {
        AuthorizedCapturedOrder(29.00m, out var payment);
        var refund = payment.AddRefund("key-a", 10.00m, "USD");

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-a"));
        Assert.Null(payment.FindRefundByIdempotencyKey("unknown"));
    }

    [Fact]
    public void Refund_CarriesGloballyUniqueGatewayRequestId()
    {
        AuthorizedCapturedOrder(29.00m, out var payment);
        var r1 = payment.AddRefund("key-a", 1.00m, "USD");
        var r2 = payment.AddRefund("key-b", 1.00m, "USD");

        Assert.False(string.IsNullOrEmpty(r1.GatewayRequestId));
        Assert.NotEqual(r1.GatewayRequestId, r2.GatewayRequestId);
        Assert.NotEqual(r1.GatewayRequestId, r1.IdempotencyKey);
    }

    [Fact]
    public void CannotRefund_BeforeCapture()
    {
        var items = new List<OrderItem> { new OrderItem(new CatalogItemOrdered(1, "Item", "p.png"), 10m, 1) };
        var order = new Order("buyer@test", new Address("s", "c", "st", "co", "z"), items);
        var payment = order.StartPayment("USD");
        payment.SetAuthorization("PPO", "AUTH", "CREATED", null, null);

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key", 1m, "USD"));
    }
}
