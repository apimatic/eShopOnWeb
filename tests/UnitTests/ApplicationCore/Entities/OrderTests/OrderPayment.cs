using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void RemainingRefundableTracksCapturedAndRefundedAmounts()
    {
        var order = AuthorizedThenCaptured();

        Assert.Equal(3.69m, order.RemainingRefundable());

        order.RecordRefund("key-1", "REFUND1", "COMPLETED", 1.00m, "USD");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RemainingRefundable());

        order.RecordRefund("key-2", "REFUND2", "COMPLETED", 2.69m, "USD");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void RepeatingRefundIdempotencyKeyDoesNotRefundTwice()
    {
        var order = AuthorizedThenCaptured();
        var first = order.RecordRefund("same-key", "REFUND1", "COMPLETED", 1.00m, "USD");
        var second = order.RecordRefund("same-key", "REFUND1", "COMPLETED", 1.00m, "USD");

        Assert.Same(first, second);
        Assert.Equal(1.00m, order.TotalRefunded());
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var order = AuthorizedThenCaptured();
        Assert.Throws<CheckoutException>(() => order.RecordRefund("key", "REFUND1", "COMPLETED", 99m, "USD"));
    }

    private static Order AuthorizedThenCaptured()
    {
        var builder = new OrderBuilder();
        var order = builder.WithDefaultValues();
        order.MarkAwaitingPayment("USD");
        order.RecordAuthorization("PPORDER", "AUTH1", "CREATED", null, null);
        order.RecordCapture("CAPTURE1", "COMPLETED", order.Total(), 0.20m, order.Total() - 0.20m);
        return order;
    }
}
