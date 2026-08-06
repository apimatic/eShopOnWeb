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
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Null(order.PaymentCaptureId);
        Assert.Null(order.PaymentRefundId);
    }

    [Fact]
    public void MarkAsPaidRecordsCaptureAndStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.MarkAsPaid("PAYPAL-ORDER-1", "CAPTURE-1");

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PAYPAL-ORDER-1", order.PaymentOrderId);
        Assert.Equal("CAPTURE-1", order.PaymentCaptureId);
    }

    [Fact]
    public void MarkAsPaidIsIdempotent_NoDoubleCharge()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PAYPAL-ORDER-1", "CAPTURE-1");

        // A duplicate capture (e.g. a double-click) must not overwrite or re-record the payment.
        order.MarkAsPaid("PAYPAL-ORDER-2", "CAPTURE-2");

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("CAPTURE-1", order.PaymentCaptureId);
    }

    [Fact]
    public void MarkAsRefundedRequiresPaidOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.MarkAsRefunded("REFUND-1"));
    }

    [Fact]
    public void MarkAsRefundedRecordsRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PAYPAL-ORDER-1", "CAPTURE-1");

        order.MarkAsRefunded("REFUND-1");

        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("REFUND-1", order.PaymentRefundId);
    }

    [Fact]
    public void MarkAsRefundedIsIdempotent_NoDoubleRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        order.MarkAsRefunded("REFUND-1");

        // A duplicate refund (e.g. a double-click) must not overwrite or re-record the refund.
        order.MarkAsRefunded("REFUND-2");

        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("REFUND-1", order.PaymentRefundId);
    }

    [Fact]
    public void CannotPayARefundedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        order.MarkAsRefunded("REFUND-1");

        Assert.Throws<InvalidOperationException>(() => order.MarkAsPaid("PAYPAL-ORDER-9", "CAPTURE-9"));
    }
}
