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
        Assert.Null(_order.PayPalOrderId);
        Assert.Null(_order.PayPalCaptureId);
        Assert.Null(_order.PayPalRefundId);
    }

    [Fact]
    public void NewOrderHasAUniquePaymentReference()
    {
        var other = new OrderBuilder().WithDefaultValues();

        Assert.NotEqual(Guid.Empty, _order.PaymentReference);
        Assert.NotEqual(_order.PaymentReference, other.PaymentReference);
    }

    [Fact]
    public void MarkPaidRecordsPayPalIdsAndTransitionsToPaid()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPORDER1", _order.PayPalOrderId);
        Assert.Equal("CAPTURE1", _order.PayPalCaptureId);
    }

    [Fact]
    public void MarkPaidTwiceThrows()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");

        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PPORDER2", "CAPTURE2"));
    }

    [Fact]
    public void MarkRefundedRequiresPaidOrder()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REFUND1"));
    }

    [Fact]
    public void MarkRefundedRecordsRefundAndTransitionsToRefunded()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");

        _order.MarkRefunded("REFUND1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REFUND1", _order.PayPalRefundId);
    }

    [Fact]
    public void MarkRefundedTwiceThrows()
    {
        _order.MarkPaid("PPORDER1", "CAPTURE1");
        _order.MarkRefunded("REFUND1");

        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REFUND2"));
    }
}
