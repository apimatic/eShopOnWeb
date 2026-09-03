using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewOrderAwaitsPaymentAndHasUniqueProviderReference()
    {
        var first = new OrderBuilder().WithDefaultValues();
        var second = new OrderBuilder().WithDefaultValues();

        Assert.Equal(PaymentStatus.AwaitingPayment, first.PaymentStatus);
        Assert.Equal(FulfillmentStatus.AwaitingFulfillment, first.FulfillmentStatus);
        Assert.StartsWith("eshop-", first.PaymentReference);
        Assert.NotEqual(first.PaymentReference, second.PaymentReference);
    }

    [Fact]
    public void CaptureRecordsProviderMoneyAndFulfilment()
    {
        var order = AuthorizedOrder();

        order.RecordCapture("capture-1", "COMPLETED", order.Total(), 0.42m,
            order.Total() - 0.42m, DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.Fulfilled, order.FulfillmentStatus);
        Assert.Equal(order.Total(), order.CapturedAmount);
        Assert.Equal(0.42m, order.PayPalFee);
        Assert.Equal(order.Total() - 0.42m, order.NetProceeds);
    }

    [Fact]
    public void PartialRefundsCannotExceedCapturedAmount()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("capture-1", "COMPLETED", order.Total(), 0.42m,
            order.Total() - 0.42m, DateTimeOffset.UtcNow);

        order.AddRefund("first", "refund-1", 1m, "COMPLETED", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(order.Total() - 1m, order.RefundableAmount);
        Assert.Throws<InvalidOperationException>(() => order.AddRefund("too-much", "refund-2",
            order.RefundableAmount + 0.01m, "COMPLETED", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FinalRefundExhaustsRefundableBalance()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("capture-1", "COMPLETED", order.Total(), 0.42m,
            order.Total() - 0.42m, DateTimeOffset.UtcNow);
        order.AddRefund("first", "refund-1", 1m, "COMPLETED", DateTimeOffset.UtcNow);

        order.AddRefund("second", "refund-2", order.RefundableAmount, "COMPLETED",
            DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableAmount);
    }

    [Fact]
    public void PendingRefundReservesButDoesNotReportReturnedMoney()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("capture-1", "COMPLETED", order.Total(), 0.42m,
            order.Total() - 0.42m, DateTimeOffset.UtcNow);

        order.AddRefund("pending", "refund-pending", 1m, "PENDING", DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.RefundPending, order.PaymentStatus);
        Assert.Equal(0m, order.RefundedAmount);
        Assert.Equal(order.Total() - 1m, order.RefundableAmount);

        order.UpdateRefundStatus("refund-pending", "FAILED");

        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(order.Total(), order.RefundableAmount);
    }

    [Fact]
    public void PendingAuthorizationCanBeRefreshedWithoutLosingProviderIds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("USD", "paypal-order-1", "authorization-1", "PENDING",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), null);

        Assert.Equal(PaymentStatus.AuthorizationPending, order.PaymentStatus);

        order.RefreshAuthorization("CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));

        Assert.Equal(PaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("authorization-1", order.AuthorizationId);
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("USD", "paypal-order-1", "authorization-1", "CREATED",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), null);
        return order;
    }
}
