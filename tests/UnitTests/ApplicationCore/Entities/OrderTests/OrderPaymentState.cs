using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
    public void AuthorizeThenFulfilThenRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", 3.69m, "USD", null, null, null);
        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.Payment.CapturedAmount);
        Assert.Equal(0.11m, order.Payment.PaypalFee);
        Assert.Equal(3.58m, order.Payment.NetProceeds);

        var first = order.RecordRefund("REF-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RemainingRefundable());
        Assert.Same(first, order.RecordRefund("REF-1", "COMPLETED", 1.00m, "USD", "key-1"));

        order.RecordRefund("REF-2", "COMPLETED", 2.69m, "USD", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", 3.69m, "USD", null, null, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Throws<InvalidOrderStateException>(() =>
            order.RecordRefund("REF-1", "COMPLETED", 4.00m, "USD", "key-1"));
    }

    [Fact]
    public void CancelReleasesUnfulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", 3.69m, "USD", null, null, null);
        order.Cancel("VOIDED");
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        order.Cancel("VOIDED");
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", 3.69m, "USD", null, null, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Throws<InvalidOrderStateException>(() => order.Cancel());
    }
}
