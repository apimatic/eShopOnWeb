using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void RecordsAuthorizationAndCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayPalOrder("PAYPAL-ORDER", "req-auth");
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        Assert.Equal(OrderPaymentStatus.Authorized, order.Status);

        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m, "USD", "req-cap");
        Assert.Equal(OrderPaymentStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.11m, order.PaypalFee);
        Assert.Equal(3.58m, order.NetAmount);
        Assert.Equal(3.69m, order.RemainingRefundable());
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 10.00m, 0.30m, 9.70m, "USD", "req-cap");

        order.AddRefund("R-1", 4.00m, "USD", "COMPLETED", "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.Status);
        Assert.Equal(6.00m, order.RemainingRefundable());

        var replay = order.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(replay);
        Assert.Equal("R-1", replay!.PayPalRefundId);

        order.AddRefund("R-2", 6.00m, "USD", "COMPLETED", "key-2");
        Assert.Equal(OrderPaymentStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CancelBeforeFulfilmentMarksCancelled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        order.MarkCancelled("req-void");
        Assert.Equal(OrderPaymentStatus.Cancelled, order.Status);
    }
}
