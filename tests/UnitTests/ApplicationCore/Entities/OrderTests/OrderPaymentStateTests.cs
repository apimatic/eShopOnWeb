using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStateTests
{
    private readonly Order _order = new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderAwaitsPaymentAndHasAReference()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, _order.PaymentStatus);
        Assert.False(string.IsNullOrEmpty(_order.PaymentReference));
    }

    [Fact]
    public void MarkPaidRecordsCaptureAndMovesToPaid()
    {
        var now = DateTimeOffset.UtcNow;
        _order.MarkPaid("PPO-1", "CAP-1", now);

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPO-1", _order.PayPalOrderId);
        Assert.Equal("CAP-1", _order.PayPalCaptureId);
        Assert.Equal(now, _order.PaidDate);
    }

    [Fact]
    public void MarkPaidIsIdempotentForTheSameCapture()
    {
        _order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        var ex = Record.Exception(() => _order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow));

        Assert.Null(ex); // repeat with the same capture is a no-op, not an error
        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
    }

    [Fact]
    public void MarkPaidOnARefundedOrderThrows()
    {
        _order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        _order.MarkRefunded("REF-1", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => _order.MarkPaid("PPO-2", "CAP-2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkRefundedRequiresAPaidOrder()
    {
        Assert.Throws<InvalidOperationException>(() => _order.MarkRefunded("REF-1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkRefundedMovesToRefundedAndIsIdempotent()
    {
        _order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        _order.MarkRefunded("REF-1", DateTimeOffset.UtcNow);

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REF-1", _order.PayPalRefundId);

        var ex = Record.Exception(() => _order.MarkRefunded("REF-1", DateTimeOffset.UtcNow));
        Assert.Null(ex);
    }
}
