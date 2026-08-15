using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentAggregateTests
{
    private static Payment NewCapturedPayment(decimal captured = 100m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer1", amount: captured, currency: "USD", payPalOrderId: "PP-ORDER");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP-1", "COMPLETED", captured, payPalFee: 3.20m, netAmount: captured - 3.20m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsPendingAuthorization()
    {
        var payment = new Payment(1, "buyer1", 10m, "USD", "PP");
        Assert.Equal(PaymentStatus.PendingAuthorization, payment.Status);
    }

    [Fact]
    public void SetAuthorizedRecordsHold()
    {
        var payment = new Payment(1, "buyer1", 10m, "USD", "PP");
        var expiry = DateTimeOffset.UtcNow.AddDays(3);
        payment.SetAuthorized("AUTH-1", "CREATED", expiry);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal(expiry, payment.AuthorizationExpiresAt);
    }

    [Fact]
    public void SetCapturedRecordsFeeBreakdownAndMakesFullAmountRefundable()
    {
        var payment = NewCapturedPayment(100m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3.20m, payment.PayPalFee);
        Assert.Equal(96.80m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableAmount);
    }

    [Fact]
    public void PartialRefundLeavesPaymentPartiallyRefundedAndReducesRefundable()
    {
        var payment = NewCapturedPayment(100m);
        payment.AddRefund("REF-1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded);
        Assert.Equal(60m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundingTheWholeCaptureMarksItRefunded()
    {
        var payment = NewCapturedPayment(100m);
        payment.AddRefund("REF-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsTheRecordedRefund()
    {
        var payment = NewCapturedPayment(100m);
        payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1");

        var found = payment.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void TwoDistinctPartialRefundsAccumulate()
    {
        var payment = NewCapturedPayment(100m);
        payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1");
        payment.AddRefund("REF-2", 15m, "COMPLETED", "key-2");

        Assert.Equal(25m, payment.TotalRefunded);
        Assert.Equal(75m, payment.RefundableAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void CannotRefundAPaymentThatIsNotCaptured()
    {
        var payment = new Payment(1, "buyer1", 10m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("REF", 5m, "COMPLETED", "k"));
    }

    [Fact]
    public void CannotVoidAfterCapture()
    {
        var payment = NewCapturedPayment(100m);
        Assert.Throws<InvalidOperationException>(() => payment.MarkVoided());
    }

    [Fact]
    public void VoidReleasesAnAuthorizedHold()
    {
        var payment = new Payment(1, "buyer1", 10m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
