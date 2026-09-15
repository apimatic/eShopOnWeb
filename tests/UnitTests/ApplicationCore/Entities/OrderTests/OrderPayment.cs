using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void RemainingRefundableTracksPartialRefunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m, "USD");

        Assert.Equal(3.69m, order.RemainingRefundable());

        order.RecordRefund("REF-1", "key-1", 1.00m, "COMPLETED");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        order.RecordRefund("REF-2", "key-2", 2.69m, "COMPLETED");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void SameRefundIdempotencyKeyIsFound()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, null, null, "USD");
        order.RecordRefund("REF-1", "idem-a", 1.00m, "COMPLETED");

        Assert.NotNull(order.FindRefundByIdempotencyKey("idem-a"));
        Assert.Null(order.FindRefundByIdempotencyKey("idem-b"));
    }
}
