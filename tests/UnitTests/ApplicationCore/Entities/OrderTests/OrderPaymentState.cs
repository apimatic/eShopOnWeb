using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        Assert.Null(_order.PayPalCaptureId);
        Assert.Null(_order.PaidDate);
    }

    [Fact]
    public void MarkPaidRecordsCaptureAndMovesToPaid()
    {
        _order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");

        Assert.Equal(OrderPaymentStatus.Paid, _order.PaymentStatus);
        Assert.Equal("PPO-1", _order.PayPalOrderId);
        Assert.Equal("CAP-1", _order.PayPalCaptureId);
        Assert.Equal("VISA ending in 1111", _order.PaymentCardDescription);
        Assert.NotNull(_order.PaidDate);
    }

    [Fact]
    public void MarkPaidTwiceThrows()
    {
        _order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");

        Assert.Throws<PaymentException>(() => _order.MarkPaid("PPO-2", "CAP-2", "VISA ending in 1111"));
    }

    [Fact]
    public void MarkRefundedBeforePaidThrows()
    {
        Assert.Throws<PaymentException>(() => _order.MarkRefunded("REF-1"));
    }

    [Fact]
    public void MarkRefundedAfterPaidMovesToRefunded()
    {
        _order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");
        _order.MarkRefunded("REF-1");

        Assert.Equal(OrderPaymentStatus.Refunded, _order.PaymentStatus);
        Assert.Equal("REF-1", _order.PayPalRefundId);
        Assert.NotNull(_order.RefundedDate);
    }

    [Fact]
    public void MarkRefundedTwiceThrows()
    {
        _order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");
        _order.MarkRefunded("REF-1");

        Assert.Throws<PaymentException>(() => _order.MarkRefunded("REF-2"));
    }

    [Fact]
    public void PayingAfterRefundThrows()
    {
        _order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");
        _order.MarkRefunded("REF-1");

        Assert.Throws<PaymentException>(() => _order.MarkPaid("PPO-2", "CAP-2", "VISA"));
    }
}
