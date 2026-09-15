using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewOrderAwaitsPaymentAndFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Pending, order.FulfilmentStatus);
    }

    [Fact]
    public void AuthorizedOrderCanBeCapturedThenRefundedInParts()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized();
        order.MarkFulfilled();
        order.MarkRefunded(false);

        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, order.FulfilmentStatus);

        order.MarkRefunded(true);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public void PaymentTracksRefundedAmountWithoutCardData()
    {
        var payment = new OrderPayment(1, "USD", 17m);
        payment.RecordAuthorization("paypal-order", "authorization", "CREATED",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), null);
        payment.RecordCapture("capture", "COMPLETED", 17m, 0.93m, 16.07m, DateTimeOffset.UtcNow);
        payment.AddRefund("part-1", "refund-1", "COMPLETED", 5m);
        payment.AddRefund("part-2", "refund-2", "COMPLETED", 12m);

        Assert.Equal(17m, payment.RefundedAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }
}
