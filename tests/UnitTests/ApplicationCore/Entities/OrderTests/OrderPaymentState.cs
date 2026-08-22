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
        Assert.Equal(ShopOrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RemainingRefundableTracksCaptureAndRefunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("paypal-order", "auth-1", "CREATED", null, null, "c", "a", "USD");
        order.MarkFulfilled("cap-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD", "capture");

        Assert.Equal(10.00m, order.RemainingRefundable());

        order.AddRefund("r1", "COMPLETED", 4.00m, "USD", "key-1", 4.00m);
        Assert.Equal(ShopOrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(6.00m, order.RemainingRefundable());

        order.AddRefund("r2", "COMPLETED", 6.00m, "USD", "key-2", 10.00m);
        Assert.Equal(ShopOrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void IdempotentRefundLookupUsesCallerKey()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("paypal-order", "auth-1", "CREATED", null, null, "c", "a", "USD");
        order.MarkFulfilled("cap-1", "COMPLETED", 10.00m, 0.50m, 9.50m, "USD", "capture");
        order.AddRefund("r1", "COMPLETED", 3.00m, "USD", "same-key", 3.00m);

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("r1", found!.PayPalRefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("other-key"));
    }
}
