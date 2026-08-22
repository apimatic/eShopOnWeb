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
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void AuthorizeThenFulfilRecordsCaptureBreakdown()
    {
        var order = PaidOrder();
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.PayPalCaptureId);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PaypalFee);
        Assert.Equal(3.28m, order.NetProceeds);
        Assert.Equal(3.69m, order.RemainingRefundable);
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapture()
    {
        var order = FulfilledOrder();
        var first = order.RecordRefund("R1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(1.00m, order.RefundedAmount);
        Assert.Equal(2.69m, order.RemainingRefundable);
        Assert.Equal(first, order.FindRefundByIdempotencyKey("key-1"));

        var replay = order.RecordRefund("R1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(first.PayPalRefundId, replay.PayPalRefundId);
        Assert.Equal(1.00m, order.RefundedAmount);

        var over = Assert.Throws<InvalidOperationException>(() =>
            order.RecordRefund("R3", "COMPLETED", 3.00m, "USD", "key-3"));
        Assert.Contains("exceeds", over.Message);

        order.RecordRefund("R2", "COMPLETED", 2.69m, "USD", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable);
    }

    [Fact]
    public void CancelIsRejectedAfterFulfilment()
    {
        var order = FulfilledOrder();
        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void AuthorizeIsIdempotentForAlreadyAuthorizedOrder()
    {
        var order = PaidOrder();
        order.MarkAuthorized("PO-2", "AUTH-2", "CREATED", 3.69m, "USD", null);
        Assert.Equal("AUTH-2", order.PayPalAuthorizationId);
        Assert.Equal(OrderStatus.Authorized, order.Status);
    }

    private static Order PaidOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PO-1", "AUTH-1", "CREATED", 3.69m, "USD", null);
        return order;
    }

    private static Order FulfilledOrder()
    {
        var order = PaidOrder();
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        return order;
    }
}
