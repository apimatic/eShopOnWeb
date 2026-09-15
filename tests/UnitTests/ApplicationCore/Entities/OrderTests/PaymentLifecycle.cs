using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewOrderAwaitsPaymentAndFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Pending, order.FulfilmentStatus);
        Assert.Null(order.Payment);
        Assert.Equal(32, order.PaymentReference.Length);
    }

    [Fact]
    public void AuthorizationMustEqualOrderTotal()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.RecordAuthorization(
            "paypal-order", "authorization", "CREATED", order.Total() - 0.01m,
            "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
    }

    [Fact]
    public void CaptureAndDistinctPartialRefundsPreserveFinancialState()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("paypal-order", "authorization", "CREATED",
            order.Total(), "USD", now, now.AddDays(29));
        order.MarkFulfilled("capture", "COMPLETED", order.Total(), 0.50m,
            order.Total() - 0.50m, now.AddMinutes(1));

        var first = order.RecordRefund("first", "refund-one", "COMPLETED", 1m, now.AddMinutes(2));
        var replay = order.RecordRefund("first", "refund-one", "COMPLETED", 1m, now.AddMinutes(2));
        order.RecordRefund("second", "refund-two", "COMPLETED", 1m, now.AddMinutes(3));

        Assert.Same(first, replay);
        Assert.Equal(2m, order.Payment!.RefundedAmount);
        Assert.Equal(2, order.Payment.Refunds.Count);
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, order.FulfilmentStatus);
        Assert.Throws<InvalidOperationException>(() => order.RecordRefund(
            "too-much", "refund-three", "COMPLETED", order.Total(), now.AddMinutes(4)));
    }
}
