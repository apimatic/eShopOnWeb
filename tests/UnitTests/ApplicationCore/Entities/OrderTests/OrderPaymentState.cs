using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void RemainingRefundableTracksPartialRefunds()
    {
        var order = AuthorizedAndCaptured(20.00m);
        order.RecordRefund("re_1", "key-1", 5.00m, "COMPLETED");

        Assert.Equal(OrderFulfillmentStatus.PartiallyRefunded, order.FulfillmentStatus);
        Assert.Equal(15.00m, order.RemainingRefundable());
    }

    [Fact]
    public void RemainingRefundableIsZeroAfterFullRefund()
    {
        var order = AuthorizedAndCaptured(20.00m);
        order.RecordRefund("re_1", "key-1", 20.00m, "COMPLETED");

        Assert.Equal(OrderFulfillmentStatus.Refunded, order.FulfillmentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void FailedRefundDoesNotReduceRemaining()
    {
        var order = AuthorizedAndCaptured(20.00m);
        order.RecordRefund("re_1", "key-1", 20.00m, "FAILED");

        Assert.Equal(20.00m, order.RemainingRefundable());
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var order = AuthorizedAndCaptured(20.00m);
        var refund = order.RecordRefund("re_1", "idem-abc", 4.00m, "COMPLETED");

        Assert.Same(refund, order.FindRefundByIdempotencyKey("idem-abc"));
        Assert.Null(order.FindRefundByIdempotencyKey("other"));
    }

    private static Order AuthorizedAndCaptured(decimal amount)
    {
        var order = new OrderBuilder().WithItems(new List<OrderItem>
        {
            new(new OrderBuilder().TestCatalogItemOrdered, amount, 1)
        });
        order.RecordAuthorization("AUTH-1", "CREATED", null, "COMPLETED", "USD");
        order.RecordCapture("CAP-1", "COMPLETED", amount, 0.59m, amount - 0.59m, "CAPTURED");
        return order;
    }
}
