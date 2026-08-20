using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
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
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 12.00m, 0.50m, 11.50m);

        order.RecordRefund("RF-1", "COMPLETED", 4.00m, "USD", "key-1");

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(8.00m, order.RemainingRefundable());
    }

    [Fact]
    public void RejectsRefundBeyondCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 5.00m, 0.20m, 4.80m);

        Assert.Throws<CheckoutException>(() => order.RecordRefund("RF-1", "COMPLETED", 5.01m, "USD", "key-1"));
    }

    [Fact]
    public void IdempotentRefundLookup()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 5.00m, 0.20m, 4.80m);
        order.RecordRefund("RF-1", "COMPLETED", 2.00m, "USD", "same-key");

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("RF-1", found!.PayPalRefundId);
    }
}
