using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().Build();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.AuthorizationId);
        Assert.Equal(0, order.RefundedAmountCents);
    }

    [Fact]
    public void FulfilmentRecordsFinixCaptureAndNetProceeds()
    {
        var order = new OrderBuilder().Build();

        order.MarkAuthorized("AU_test", "SUCCEEDED", "PI_test", null, 1000, "USD");
        order.MarkFulfilled("TR_test", "SUCCEEDED", 1000, 79, 921);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(1000, order.CapturedAmountCents);
        Assert.Equal(79, order.FinixFeeCents);
        Assert.Equal(921, order.NetProceedsCents);
    }

    [Fact]
    public void PartialRefundsAccumulateAndBecomeFullyRefunded()
    {
        var order = new OrderBuilder().Build();
        order.MarkAuthorized("AU_test", "SUCCEEDED", "PI_test", null, 1000, "USD");
        order.MarkFulfilled("TR_test", "SUCCEEDED", 1000, 79, 921);

        var first = order.AddRefund("TR_refund_1", 300, "SUCCEEDED", "key-1", DateTimeOffset.UtcNow);
        Assert.Equal(300, order.RefundedAmountCents);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Same(first, order.FindRefundByIdempotencyKey("key-1"));

        order.AddRefund("TR_refund_2", 700, "SUCCEEDED", "key-2", DateTimeOffset.UtcNow);
        Assert.Equal(1000, order.RefundedAmountCents);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }
}
