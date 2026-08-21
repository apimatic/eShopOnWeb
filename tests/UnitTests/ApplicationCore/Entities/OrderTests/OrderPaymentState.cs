using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void RemainingRefundableTracksPartialRefunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.20m, 3.49m, 3.69m);

        order.RecordRefund("REF-1", "COMPLETED", 1.00m, "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var duplicate = order.RecordRefund("REF-1-AGAIN", "COMPLETED", 1.00m, "key-1");
        Assert.Equal("REF-1", duplicate.PayPalRefundId);
        Assert.Equal(2.69m, order.RemainingRefundable());

        Assert.Throws<InvalidOperationException>(() =>
            order.RecordRefund("REF-2", "COMPLETED", 9.00m, "key-2"));

        order.RecordRefund("REF-3", "COMPLETED", 2.69m, "key-3");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CannotFulfilAfterCancel()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, "USD");
        order.RecordVoid();
        Assert.Throws<InvalidOperationException>(() =>
            order.RecordCapture("CAP-1", "COMPLETED", 1m, null, null, null));
    }
}
