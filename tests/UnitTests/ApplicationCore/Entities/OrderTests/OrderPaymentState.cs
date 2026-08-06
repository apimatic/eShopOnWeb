using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private readonly Order _order = new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderIsAwaitingPaymentInUsd()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, _order.PaymentStatus);
        Assert.Equal("USD", _order.Currency);
        Assert.Null(_order.PaymentCaptureId);
    }

    [Fact]
    public void MarkPaidRecordsProviderIdsAndStatus()
    {
        _order.MarkPaid("PPORDER1", "CAP1");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPORDER1", _order.PaymentProviderOrderId);
        Assert.Equal("CAP1", _order.PaymentCaptureId);
        Assert.Equal("PayPal", _order.PaymentProvider);
        Assert.NotNull(_order.PaidDate);
    }

    [Fact]
    public void MarkPaidIsIdempotentForSameCapture()
    {
        _order.MarkPaid("PPORDER1", "CAP1");
        var paidAt = _order.PaidDate;

        // A replayed payment with the same capture is a no-op, not an error or a second charge.
        _order.MarkPaid("PPORDER1", "CAP1");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal(paidAt, _order.PaidDate);
    }

    [Fact]
    public void MarkPaidWithDifferentCaptureOnPaidOrderThrows()
    {
        _order.MarkPaid("PPORDER1", "CAP1");
        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PPORDER2", "CAP2"));
    }

    [Fact]
    public void MarkRefundedRequiresPaid()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REF1"));
    }

    [Fact]
    public void MarkRefundedRecordsRefund()
    {
        _order.MarkPaid("PPORDER1", "CAP1");
        _order.MarkRefunded("REF1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REF1", _order.PaymentRefundId);
        Assert.NotNull(_order.RefundedDate);
    }

    [Fact]
    public void MarkRefundedIsIdempotentForSameRefund()
    {
        _order.MarkPaid("PPORDER1", "CAP1");
        _order.MarkRefunded("REF1");
        var refundedAt = _order.RefundedDate;

        _order.MarkRefunded("REF1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal(refundedAt, _order.RefundedDate);
    }

    [Fact]
    public void PayingARefundedOrderThrows()
    {
        _order.MarkPaid("PPORDER1", "CAP1");
        _order.MarkRefunded("REF1");
        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PPORDER3", "CAP3"));
    }
}
