using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void ReservesRefundsAndRejectsAnAmountBeyondTheCapture()
    {
        var payment = PaidOrder().Payment!;

        var first = payment.BeginRefund("first", "provider-first", 6m);
        payment.RecordRefund(first, "refund-first", "COMPLETED", 6m);

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Throws<InvalidOperationException>(() =>
            payment.BeginRefund("too-much", "provider-too-much", 4.01m));
    }

    [Fact]
    public void ReturnsTheSameRefundForTheSameIdempotencyKey()
    {
        var payment = PaidOrder().Payment!;

        var first = payment.BeginRefund("same-key", "provider-one", 4m);
        var repeated = payment.BeginRefund("same-key", "provider-two", 4m);

        Assert.Same(first, repeated);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void RecordsProviderCaptureEconomics()
    {
        var payment = PaidOrder().Payment!;

        Assert.Equal(OrderPaymentStatus.Captured, payment.Status);
        Assert.Equal(10m, payment.CapturedAmount);
        Assert.Equal(0.59m, payment.PayPalFee);
        Assert.Equal(9.41m, payment.NetAmount);
        Assert.NotNull(payment.CapturedAt);
    }

    [Fact]
    public void DistinguishesAPendingRefundFromACompletedRefund()
    {
        var payment = PaidOrder().Payment!;
        var refund = payment.BeginRefund("pending", "provider-pending", 10m);

        payment.RecordRefund(refund, "refund", "PENDING", 10m);
        Assert.Equal(OrderPaymentStatus.RefundPending, payment.Status);

        payment.RecordRefund(refund, "refund", "COMPLETED", 10m);
        Assert.Equal(OrderPaymentStatus.Refunded, payment.Status);
    }

    private static Order PaidOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "test.png"), 10m, 1);
        var order = new Order("buyer", new Address("1 Main", "City", "State", "US", "12345"),
            new List<OrderItem> { item }, "USD");
        var payment = order.Payment!;
        payment.BeginAuthorization("provider-order", null);
        payment.RecordAuthorization("authorization", "CREATED", 10m, DateTimeOffset.UtcNow.AddDays(3));
        payment.BeginCapture();
        payment.RecordCapture("capture", "COMPLETED", 10m, 0.59m, 9.41m);
        return order;
    }
}
