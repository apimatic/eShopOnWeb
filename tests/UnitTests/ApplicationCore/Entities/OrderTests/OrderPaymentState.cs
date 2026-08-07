using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private readonly Order _order = new OrderBuilder().WithDefaultValues();

    [Fact]
    public void StartsAwaitingPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, _order.PaymentStatus);
        Assert.Null(_order.PaymentCaptureId);
    }

    [Fact]
    public void MarkAsPaidRecordsPayPalIdentifiers()
    {
        _order.MarkAsPaid("PPO-1", "CAP-1");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPO-1", _order.PayPalOrderId);
        Assert.Equal("CAP-1", _order.PaymentCaptureId);
        Assert.NotNull(_order.PaidDate);
    }

    [Fact]
    public void MarkAsPaidIsIdempotentForSameCapture()
    {
        _order.MarkAsPaid("PPO-1", "CAP-1");
        var firstPaidDate = _order.PaidDate;

        _order.MarkAsPaid("PPO-1", "CAP-1"); // repeated, e.g. double-click

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal(firstPaidDate, _order.PaidDate);
    }

    [Fact]
    public void MarkAsRefundedRequiresPaidState()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkAsRefunded("REF-1"));
    }

    [Fact]
    public void MarkAsRefundedTransitionsPaidOrder()
    {
        _order.MarkAsPaid("PPO-1", "CAP-1");

        _order.MarkAsRefunded("REF-1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REF-1", _order.PaymentRefundId);
        Assert.NotNull(_order.RefundedDate);
    }

    [Fact]
    public void MarkAsRefundedIsIdempotentForSameRefund()
    {
        _order.MarkAsPaid("PPO-1", "CAP-1");
        _order.MarkAsRefunded("REF-1");
        var firstRefundedDate = _order.RefundedDate;

        _order.MarkAsRefunded("REF-1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal(firstRefundedDate, _order.RefundedDate);
    }

    [Fact]
    public void CannotPayAfterRefund()
    {
        _order.MarkAsPaid("PPO-1", "CAP-1");
        _order.MarkAsRefunded("REF-1");

        Assert.Throws<InvalidOperationException>(() => _order.MarkAsPaid("PPO-2", "CAP-2"));
    }
}
