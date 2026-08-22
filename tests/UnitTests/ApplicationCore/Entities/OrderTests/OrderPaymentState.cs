using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RemainingRefundableIsZeroUntilCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void RemainingRefundableSubtractsCompletedRefunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, null, "USD", "ESHOP-1");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 10.00m, 0.59m, 9.41m);

        order.AddRefund(new OrderRefund("R1", "COMPLETED", 4.00m, "USD", "key-1"));

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(6.00m, order.RemainingRefundable());

        order.AddRefund(new OrderRefund("R2", "COMPLETED", 6.00m, "USD", "key-2"));
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void DuplicateIdempotencyKeyCanBeFound()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, null, "USD", "ESHOP-1");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 10.00m, 0.59m, 9.41m);
        order.AddRefund(new OrderRefund("R1", "COMPLETED", 2.00m, "USD", "same-key"));

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("R1", found!.PaypalRefundId);
    }

    [Fact]
    public void TotalStillUsesCatalogLineItems()
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, 19.50m, 2)
        };
        var order = builder.WithItems(items);
        Assert.Equal(39.00m, order.Total());
    }
}
