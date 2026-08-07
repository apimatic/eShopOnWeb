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
        Assert.Equal(PaymentStatus.AwaitingPayment, _order.PaymentStatus);
        Assert.Null(_order.PaymentCaptureId);
        Assert.NotEqual(Guid.Empty, _order.IdempotencyToken);
    }

    [Fact]
    public void MarkPaidTransitionsToPaidAndRecordsIds()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");

        Assert.Equal(PaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPORDER1", _order.PayPalOrderId);
        Assert.Equal("CAPTURE1", _order.PaymentCaptureId);
    }

    [Fact]
    public void MarkPaidIsIdempotentForSameCapture()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");
        _order.MarkPaid("PPORDER1", "CAPTURE1"); // must not throw

        Assert.Equal(PaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("CAPTURE1", _order.PaymentCaptureId);
    }

    [Fact]
    public void MarkPaidOnRefundedOrderThrows()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");
        _order.MarkRefunded("REFUND1");

        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PPORDER2", "CAPTURE2"));
    }

    [Fact]
    public void MarkRefundedRequiresPaid()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REFUND1"));
    }

    [Fact]
    public void MarkRefundedTransitionsToRefunded()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");
        _order.MarkRefunded("REFUND1");

        Assert.Equal(PaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REFUND1", _order.PaymentRefundId);
    }

    [Fact]
    public void MarkRefundedIsIdempotent()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");
        _order.MarkRefunded("REFUND1");
        _order.MarkRefunded("REFUND1"); // must not throw

        Assert.Equal(PaymentStatus.Refunded, _order.PaymentStatus);
    }
}
