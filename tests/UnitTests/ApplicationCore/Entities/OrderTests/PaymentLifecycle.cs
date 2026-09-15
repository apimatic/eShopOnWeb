using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewOrderAwaitsPaymentAndHasUniqueReference()
    {
        var first = new OrderBuilder().Build();
        var second = new OrderBuilder().Build();

        Assert.Equal(OrderStatus.AwaitingPayment, first.Status);
        Assert.Equal(PaymentStatus.AwaitingPayment, first.PaymentStatus);
        Assert.StartsWith("eshop-", first.PaymentReference);
        Assert.NotEqual(first.PaymentReference, second.PaymentReference);
    }

    [Fact]
    public void CapturedOrderRecordsPayPalEconomicsAndCapsRefunds()
    {
        var order = new OrderBuilder().Build();
        var total = order.Total();
        var now = DateTimeOffset.UtcNow;
        order.RecordAuthorization("USD", "paypal-order", "COMPLETED", "authorization",
            "CREATED", total, now, now.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", total, 0.50m, total - 0.50m, now);

        var first = order.RecordRefund("key-one", "refund-one", "COMPLETED", 1m, now);
        var duplicate = order.RecordRefund("key-one", "refund-one", "COMPLETED", 1m, now);

        Assert.Same(first, duplicate);
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(1m, order.RefundedAmount);
        Assert.Throws<InvalidOperationException>(() =>
            order.RecordRefund("key-two", "refund-two", "COMPLETED", total, now));
    }

    [Fact]
    public void AuthorizationMustEqualOrderTotal()
    {
        var order = new OrderBuilder().Build();
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<InvalidOperationException>(() => order.RecordAuthorization("USD",
            "paypal-order", "COMPLETED", "authorization", "CREATED", order.Total() + 0.01m,
            now, now.AddDays(29)));
    }
}
