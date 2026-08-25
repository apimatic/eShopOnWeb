using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentRefunds
{
    private static OrderPayment CapturedPayment(decimal capturedAmount)
    {
        var payment = new OrderPayment(orderId: 1, authorizedAmount: capturedAmount, currencyCode: "USD");
        payment.RecordAuthorization("paypal-order-1", "auth-1", "CREATED", null, null);
        payment.RecordCapture("capture-1", "COMPLETED", capturedAmount, 1.5m, capturedAmount - 1.5m);
        return payment;
    }

    [Fact]
    public void AddRefundThrowsBeforeCapture()
    {
        var payment = new OrderPayment(orderId: 1, authorizedAmount: 100m, currencyCode: "USD");

        Assert.Throws<OrderPaymentStateException>(() => payment.AddRefund("refund-1", 10m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void AddRefundThrowsWhenExceedingCapturedAmount()
    {
        var payment = CapturedPayment(100m);

        Assert.Throws<OrderPaymentStateException>(() => payment.AddRefund("refund-1", 150m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void TwoDistinctPartialRefundsAreBothAccepted()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("refund-2", 40m, "COMPLETED", "key-2");

        Assert.Equal(80m, payment.TotalRefundedAmount);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void RepeatingTheSameIdempotencyKeyDoesNotRefundTwice()
    {
        var payment = CapturedPayment(100m);

        var first = payment.AddRefund("refund-1", 40m, "COMPLETED", "same-key");
        var second = payment.AddRefund("refund-1", 40m, "COMPLETED", "same-key");

        Assert.Same(first, second);
        Assert.Equal(40m, payment.TotalRefundedAmount);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void FullRefundMarksPaymentRefunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(OrderPaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void SecondRefundCannotExceedWhatRemainsAfterAFirstPartialRefund()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("refund-1", 60m, "COMPLETED", "key-1");

        Assert.Throws<OrderPaymentStateException>(() => payment.AddRefund("refund-2", 60m, "COMPLETED", "key-2"));
    }
}
