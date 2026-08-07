using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private readonly Order _order = new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, _order.PaymentStatus);
        Assert.Null(_order.PaymentCaptureId);
    }

    [Fact]
    public void MarkPaidMovesToPaidAndRecordsProviderIds()
    {
        _order.MarkPaid("PAYPAL-ORDER-1", "CAPTURE-1");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PAYPAL-ORDER-1", _order.PaymentProviderOrderId);
        Assert.Equal("CAPTURE-1", _order.PaymentCaptureId);
    }

    [Fact]
    public void MarkPaidIsIdempotent()
    {
        _order.MarkPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        // A double-click replays MarkPaid; it must not throw and must keep the original capture.
        _order.MarkPaid("PAYPAL-ORDER-2", "CAPTURE-2");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("CAPTURE-1", _order.PaymentCaptureId);
    }

    [Fact]
    public void MarkRefundedRequiresPaidFirst()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REFUND-1"));
    }

    [Fact]
    public void MarkRefundedMovesToRefunded()
    {
        _order.MarkPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        _order.MarkRefunded("REFUND-1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REFUND-1", _order.PaymentRefundId);
    }

    [Fact]
    public void MarkRefundedIsIdempotent()
    {
        _order.MarkPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        _order.MarkRefunded("REFUND-1");
        // A double-click replays MarkRefunded; it must not throw and must keep the original refund.
        _order.MarkRefunded("REFUND-2");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REFUND-1", _order.PaymentRefundId);
    }

    [Fact]
    public void CannotPayARefundedOrder()
    {
        _order.MarkPaid("PAYPAL-ORDER-1", "CAPTURE-1");
        _order.MarkRefunded("REFUND-1");

        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PAYPAL-ORDER-9", "CAPTURE-9"));
    }
}
