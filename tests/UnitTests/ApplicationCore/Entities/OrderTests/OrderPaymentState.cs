using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private Order NewOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderAwaitsPaymentAndHasAReference()
    {
        var order = NewOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.NotEqual(Guid.Empty, order.PaymentReference);
        Assert.Null(order.PayPalOrderId);
        Assert.Null(order.PayPalCaptureId);
        Assert.Null(order.PayPalRefundId);
    }

    [Fact]
    public void MarkPaidRecordsPaymentAndAdvancesState()
    {
        var order = NewOrder();

        order.MarkPaid("PP-ORDER-1", "CAPTURE-1");

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PP-ORDER-1", order.PayPalOrderId);
        Assert.Equal("CAPTURE-1", order.PayPalCaptureId);
    }

    [Fact]
    public void MarkPaidIsIdempotent()
    {
        var order = NewOrder();
        order.MarkPaid("PP-ORDER-1", "CAPTURE-1");

        // A second (double-clicked) call must not overwrite the recorded payment.
        order.MarkPaid("PP-ORDER-2", "CAPTURE-2");

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PP-ORDER-1", order.PayPalOrderId);
        Assert.Equal("CAPTURE-1", order.PayPalCaptureId);
    }

    [Fact]
    public void MarkRefundedRequiresAPaidOrder()
    {
        var order = NewOrder();

        order.MarkRefunded("REFUND-1");

        // Never paid, so refund is a no-op.
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Null(order.PayPalRefundId);
    }

    [Fact]
    public void MarkRefundedAdvancesAPaidOrder()
    {
        var order = NewOrder();
        order.MarkPaid("PP-ORDER-1", "CAPTURE-1");

        order.MarkRefunded("REFUND-1");

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("REFUND-1", order.PayPalRefundId);
    }

    [Fact]
    public void MarkRefundedIsIdempotent()
    {
        var order = NewOrder();
        order.MarkPaid("PP-ORDER-1", "CAPTURE-1");
        order.MarkRefunded("REFUND-1");

        order.MarkRefunded("REFUND-2");

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("REFUND-1", order.PayPalRefundId);
    }

    [Fact]
    public void CannotPayARefundedOrder()
    {
        var order = NewOrder();
        order.MarkPaid("PP-ORDER-1", "CAPTURE-1");
        order.MarkRefunded("REFUND-1");

        order.MarkPaid("PP-ORDER-9", "CAPTURE-9");

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("PP-ORDER-1", order.PayPalOrderId);
    }
}
