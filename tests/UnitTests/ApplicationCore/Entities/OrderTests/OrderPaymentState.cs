using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Order NewOrder() => new(
        "buyer-1",
        new Address("1 St", "City", "ST", "US", "00000"),
        new List<OrderItem>());

    [Fact]
    public void StartsAwaitingPaymentWithAnIdempotencyToken()
    {
        var order = NewOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.NotEqual(Guid.Empty, order.PaymentIdempotencyToken);
    }

    [Fact]
    public void MarkPaidRecordsPayPalIdentifiers()
    {
        var order = NewOrder();

        order.MarkPaid("PPORDER1", "CAPTURE1");

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PPORDER1", order.PayPalOrderId);
        Assert.Equal("CAPTURE1", order.PayPalCaptureId);
    }

    [Fact]
    public void MarkPaidIsIdempotentForTheSameCapture()
    {
        var order = NewOrder();
        order.MarkPaid("PPORDER1", "CAPTURE1");

        order.MarkPaid("PPORDER1", "CAPTURE1"); // no throw, no change

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("CAPTURE1", order.PayPalCaptureId);
    }

    [Fact]
    public void MarkRefundedRequiresPaidOrder()
    {
        var order = NewOrder();

        Assert.Throws<InvalidOperationException>(() => order.MarkRefunded("REFUND1"));
    }

    [Fact]
    public void MarkRefundedRecordsRefundAndIsIdempotent()
    {
        var order = NewOrder();
        order.MarkPaid("PPORDER1", "CAPTURE1");

        order.MarkRefunded("REFUND1");
        order.MarkRefunded("REFUND1"); // idempotent

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("REFUND1", order.PayPalRefundId);
    }

    [Fact]
    public void RefundedOrderCannotBePaidAgain()
    {
        var order = NewOrder();
        order.MarkPaid("PPORDER1", "CAPTURE1");
        order.MarkRefunded("REFUND1");

        Assert.Throws<InvalidOperationException>(() => order.MarkPaid("PPORDER2", "CAPTURE2"));
    }

    [Fact]
    public void MarkPaymentFailedDoesNotOverrideAPaidOrder()
    {
        var order = NewOrder();
        order.MarkPaid("PPORDER1", "CAPTURE1");

        order.MarkPaymentFailed();

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
    }
}
