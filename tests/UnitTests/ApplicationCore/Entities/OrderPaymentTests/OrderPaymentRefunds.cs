using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentRefunds
{
    private static OrderPayment CapturedPayment(decimal captured = 100m)
    {
        var payment = new OrderPayment(orderId: 1, buyerId: "buyer@test", amount: captured, currencyCode: "USD",
            reference: "ESHOP-1-abc");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");
        payment.MarkCaptured("CAP-1", "COMPLETED", captured, 3m, captured - 3m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsAwaitingPayment()
    {
        var payment = new OrderPayment(1, "buyer@test", 10m, "USD", "ref");
        Assert.Equal(PaymentStatus.PendingPayment, payment.Status);
        Assert.Equal(0m, payment.TotalRefunded());
    }

    [Fact]
    public void PartialRefundLeavesOrderPartiallyRefunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new Refund("R-1", "key-1", 40m, "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded());
        Assert.Equal(60m, payment.RefundableAmount());
    }

    [Fact]
    public void RefundsSummingToCaptureMarkOrderRefunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new Refund("R-1", "key-1", 60m, "COMPLETED"));
        payment.AddRefund(new Refund("R-2", "key-2", 40m, "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new Refund("R-1", "key-1", 70m, "COMPLETED"));

        // Remaining refundable is 30; a 40 refund must never be allowed.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund(new Refund("R-2", "key-2", 40m, "COMPLETED")));
        Assert.Contains("exceeds", ex.Message);

        // State is unchanged by the rejected refund.
        Assert.Equal(70m, payment.TotalRefunded());
        Assert.Equal(30m, payment.RefundableAmount());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new OrderPayment(1, "buyer@test", 100m, "USD", "ref");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");

        Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund(new Refund("R-1", "key-1", 10m, "COMPLETED")));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExistingRefund()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund(new Refund("R-1", "key-1", 25m, "COMPLETED"));

        var found = payment.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(found);
        Assert.Equal("R-1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CancelReleasesHoldOnlyWhenAuthorized()
    {
        var payment = new OrderPayment(1, "buyer@test", 100m, "USD", "ref");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");
        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
