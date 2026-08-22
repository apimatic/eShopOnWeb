using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void AuthorizeThenFulfilRecordsCaptureTotals()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, "USD", null, null, "1111", "VISA");
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m, System.DateTimeOffset.UtcNow);

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(3.69m, order.Payment.CapturedAmount);
        Assert.Equal(0.41m, order.Payment.PaypalFee);
        Assert.Equal(3.28m, order.Payment.NetAmount);
        Assert.Equal("CAP-1", order.Payment.CaptureId);
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, "USD", null, null, "1111", "VISA");
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m, System.DateTimeOffset.UtcNow);

        var first = order.RecordRefund("key-1", "REF-1", "COMPLETED", 1.00m, "USD");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var replay = order.RecordRefund("key-1", "REF-1", "COMPLETED", 1.00m, "USD");
        Assert.Same(first, replay);
        Assert.Equal(2.69m, order.RemainingRefundable());

        Assert.Throws<CheckoutException>(() => order.RecordRefund("key-2", "REF-2", "COMPLETED", 3.00m, "USD"));

        order.RecordRefund("key-2", "REF-2", "COMPLETED", 2.69m, "USD");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CancelReleasesAuthorizationBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, "USD", null, null, "1111", "VISA");
        order.MarkCancelled(System.DateTimeOffset.UtcNow);

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.Payment.AuthorizationStatus);
        Assert.Throws<CheckoutException>(() =>
            order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m, System.DateTimeOffset.UtcNow));
    }
}
