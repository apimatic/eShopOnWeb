using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Pending, order.FulfilmentStatus);
    }

    [Fact]
    public void CaptureRecordsProviderAmountsAndFulfils()
    {
        var order = AuthorizedOrder();

        order.BeginCapture();
        order.MarkCaptured("CAPTURE-1", "COMPLETED", order.Total(), 0.25m, order.Total() - 0.25m);

        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, order.FulfilmentStatus);
        Assert.Equal(0.25m, order.PayPalFee);
        Assert.Equal(order.Total() - 0.25m, order.NetProceeds);
    }

    [Fact]
    public void SameRefundKeyIsIdempotentButCannotChangeAmount()
    {
        var order = CapturedOrder();

        var first = order.ReserveRefund("same-key", 1m);

        Assert.Same(first, order.ReserveRefund("same-key", 1m));
        Assert.Throws<InvalidOperationException>(() => order.ReserveRefund("same-key", 2m));
    }

    [Fact]
    public void RefundReservationsCannotExceedCapture()
    {
        var order = CapturedOrder();
        order.ReserveRefund("first", 2m);

        Assert.Throws<InvalidOperationException>(() => order.ReserveRefund("second", 2m));
    }

    [Fact]
    public void CompletedPartialAndFullRefundsUpdatePaymentState()
    {
        var order = CapturedOrder();
        var first = order.ReserveRefund("first", 1m);
        first.Complete("REFUND-1", "COMPLETED");
        order.RefreshRefundState();

        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);

        var second = order.ReserveRefund("second", order.Total() - 1m);
        second.Complete("REFUND-2", "COMPLETED");
        order.RefreshRefundState();

        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization();
        order.MarkAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        return order;
    }

    private static Order CapturedOrder()
    {
        var order = AuthorizedOrder();
        order.BeginCapture();
        order.MarkCaptured("CAPTURE-1", "COMPLETED", order.Total(), 0.1m, order.Total() - 0.1m);
        return order;
    }
}
