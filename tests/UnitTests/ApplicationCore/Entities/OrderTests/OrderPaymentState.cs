using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderState.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RecordsAuthorizationAndIsIdempotentOnCaptureWhenAlreadyFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, null, null);
        Assert.Equal(OrderState.Authorized, order.Status);

        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Equal(OrderState.Fulfilled, order.Status);
        Assert.Equal(0.11m, order.PaypalFee);
        Assert.Equal(3.58m, order.NetAmount);

        order.RecordCapture("CAP-2", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Equal("CAP-1", order.CaptureId);
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, null, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);

        var first = order.RecordRefund("R1", "COMPLETED", 1.00m, "key-1");
        Assert.Equal(OrderState.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RemainingRefundable());
        Assert.Same(first, order.RecordRefund("R1", "COMPLETED", 1.00m, "key-1"));

        Assert.Throws<CheckoutException>(() => order.RecordRefund("R2", "COMPLETED", 3.00m, "key-2"));

        order.RecordRefund("R3", "COMPLETED", 2.69m, "key-3");
        Assert.Equal(OrderState.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CancelRequiresAuthorizedOrAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, null, null);
        order.RecordVoid();
        Assert.Equal(OrderState.Cancelled, order.Status);

        var fulfilled = new OrderBuilder().WithDefaultValues();
        fulfilled.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, null, null);
        fulfilled.RecordCapture("CAP-1", "COMPLETED", 3.69m, null, null);
        Assert.Throws<CheckoutException>(() => fulfilled.RecordVoid());
    }
}
