using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentState
{
    [Fact]
    public void KeepsOnlyLatestAuthorizationCurrent()
    {
        var payment = new OrderPayment("usd");

        var first = payment.BeginAuthorization("Card", null);
        var second = payment.BeginAuthorization("SavedCard", 42);

        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Same(second, payment.CurrentAuthorization);
        Assert.Equal("USD", payment.Currency);
    }

    [Fact]
    public void RefundsCannotExceedCaptureOrReuseKey()
    {
        var payment = new OrderPayment("USD");
        payment.RecordCapture("capture-1", "COMPLETED", 10m, 1m, 9m, DateTimeOffset.UtcNow);

        payment.RecordRefund("refund-key-1", "refund-1", "COMPLETED", 4m, DateTimeOffset.UtcNow);

        Assert.Equal(6m, payment.RefundableAmount);
        Assert.Throws<InvalidOperationException>(() =>
            payment.RecordRefund("refund-key-2", "refund-2", "COMPLETED", 7m, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            payment.RecordRefund("refund-key-1", "refund-3", "COMPLETED", 1m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FullRefundLeavesNothingRefundable()
    {
        var payment = new OrderPayment("USD");
        payment.RecordCapture("capture-1", "COMPLETED", 12.34m, 0.34m, 12m, DateTimeOffset.UtcNow);

        payment.RecordRefund("refund-key", "refund-1", "COMPLETED", 12.34m, DateTimeOffset.UtcNow);

        Assert.Equal(0m, payment.RefundableAmount);
        Assert.Equal(12.34m, payment.RefundedAmount);
    }
}
