using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundInvariants
{
    private const string BuyerId = "buyer@example.com";
    private const string Currency = "USD";

    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(orderId: 1, BuyerId, amount, Currency);
        payment.AssignInvoiceId("ESHOP-run-1");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111");
        payment.MarkCaptured("CAP1", "COMPLETED", amount, 3.20m, amount - 3.20m);
        return payment;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var payment = new Payment(1, BuyerId, 50m, Currency);
        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new Payment(1, BuyerId, 50m, Currency);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-1", 10m));
    }

    [Fact]
    public void PartialRefundLeavesRemainingRefundable()
    {
        var payment = CapturedPayment(100m);
        var refund = payment.AddRefund("key-1", 40m);
        refund.MarkCompleted("REF1", "COMPLETED");
        payment.ApplyRefundOutcome();

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded);
        Assert.Equal(60m, payment.RefundableRemaining);
    }

    [Fact]
    public void TwoDistinctPartialRefundsAreAllowedUpToCapturedTotal()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 40m).MarkCompleted("REF1", "COMPLETED");
        payment.ApplyRefundOutcome();
        payment.AddRefund("key-2", 60m).MarkCompleted("REF2", "COMPLETED");
        payment.ApplyRefundOutcome();

        Assert.Equal(100m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 70m).MarkCompleted("REF1", "COMPLETED");
        payment.ApplyRefundOutcome();

        // 70 already refunded; a further 40 would exceed the captured 100.
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-2", 40m));
        Assert.Equal(30m, payment.RefundableRemaining);
    }

    [Fact]
    public void FullRefundMarksRefunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 100m).MarkCompleted("REF1", "COMPLETED");
        payment.ApplyRefundOutcome();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var payment = CapturedPayment(100m);
        var refund = payment.AddRefund("key-1", 10m);

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void CancelOnlyReleasesAuthorization()
    {
        var payment = new Payment(1, BuyerId, 50m, Currency);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");
        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
