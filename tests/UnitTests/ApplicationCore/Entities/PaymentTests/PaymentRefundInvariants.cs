using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundInvariants
{
    private static Payment CapturedPayment(decimal amount = 39m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@x.com", currencyCode: "USD", amount: amount, invoiceId: "INV-1");
        payment.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED");
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, payPalFee: 1.5m, netAmount: amount - 1.5m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsAwaitingPayment()
    {
        var payment = new Payment(1, "buyer@x.com", "USD", 10m, "INV-1");
        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(0m, payment.RefundedAmount);
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new Payment(1, "buyer@x.com", "USD", 10m, "INV-1");
        payment.MarkAuthorized("PP", "AUTH", "CREATED");

        var ex = Assert.Throws<PaymentException>(() => payment.AddRefund("R1", 5m, "key-1", "COMPLETED"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void PartialRefundLeavesPaymentPartiallyRefunded()
    {
        var payment = CapturedPayment(39m);

        payment.AddRefund("R1", 10m, "key-1", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.RefundedAmount);
        Assert.Equal(29m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundingTheWholeCaptureMarksRefunded()
    {
        var payment = CapturedPayment(39m);

        payment.AddRefund("R1", 20m, "key-1", "COMPLETED");
        payment.AddRefund("R2", 19m, "key-2", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void CannotRefundBeyondCapturedAmount()
    {
        var payment = CapturedPayment(39m);
        payment.AddRefund("R1", 30m, "key-1", "COMPLETED");

        // Only 9.00 remains; a 10.00 refund must be rejected to the cent.
        var ex = Assert.Throws<PaymentException>(() => payment.AddRefund("R2", 10m, "key-2", "COMPLETED"));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(30m, payment.RefundedAmount); // unchanged
    }

    [Fact]
    public void FindsRefundByIdempotencyKey()
    {
        var payment = CapturedPayment(39m);
        payment.AddRefund("R1", 5m, "key-abc", "COMPLETED");

        var found = payment.FindRefundByIdempotencyKey("key-abc");
        Assert.NotNull(found);
        Assert.Equal("R1", found!.RefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("nope"));
    }

    [Fact]
    public void CaptureRecordsFeeAndNet()
    {
        var payment = CapturedPayment(39m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(39m, payment.CapturedAmount);
        Assert.Equal(1.5m, payment.PayPalFee);
        Assert.Equal(37.5m, payment.NetAmount);
        Assert.Equal("CAP-1", payment.CaptureId);
    }

    [Fact]
    public void CancelMarksVoided()
    {
        var payment = new Payment(1, "buyer@x.com", "USD", 10m, "INV-1");
        payment.MarkAuthorized("PP", "AUTH", "CREATED");

        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }
}
