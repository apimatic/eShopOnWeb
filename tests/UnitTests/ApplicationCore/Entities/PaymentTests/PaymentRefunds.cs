using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefunds
{
    private static Payment CapturedPayment(decimal captured = 20.50m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer-1", amount: captured, currencyCode: "USD");
        payment.SetAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "Card ****1111");
        payment.SetCapture("CAP-1", "COMPLETED", captured, paypalFee: 1.02m, netAmount: captured - 1.02m);
        return payment;
    }

    [Fact]
    public void CapturedPaymentIsFullyRefundable()
    {
        var payment = CapturedPayment(20.50m);
        Assert.Equal(20.50m, payment.RefundableRemaining);
        Assert.Equal(0m, payment.TotalRefunded);
    }

    [Fact]
    public void PartialRefundReducesRemainingAndSetsStatus()
    {
        var payment = CapturedPayment(20.50m);
        payment.AddRefund("R1", 5.00m, "COMPLETED", "key-1");
        Assert.Equal(15.50m, payment.RefundableRemaining);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void RefundingTheRemainderMarksRefunded()
    {
        var payment = CapturedPayment(10m);
        payment.AddRefund("R1", 4m, "COMPLETED", "k1");
        payment.AddRefund("R2", 6m, "COMPLETED", "k2");
        Assert.Equal(0m, payment.RefundableRemaining);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var payment = CapturedPayment(10m);
        payment.AddRefund("R1", 8m, "COMPLETED", "k1");
        var ex = Assert.Throws<PaymentApiException>(() => payment.AddRefund("R2", 5m, "COMPLETED", "k2"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(2m, payment.RefundableRemaining); // unchanged after rejected refund
    }

    [Fact]
    public void ZeroOrNegativeRefundIsRejected()
    {
        var payment = CapturedPayment(10m);
        Assert.Throws<PaymentApiException>(() => payment.AddRefund("R1", 0m, "COMPLETED", "k1"));
    }

    [Fact]
    public void FindRefundByKeyReturnsTheRecordedRefund()
    {
        var payment = CapturedPayment(10m);
        var refund = payment.AddRefund("R1", 3m, "COMPLETED", "idem-key");
        Assert.Same(refund, payment.FindRefundByKey("idem-key"));
        Assert.Null(payment.FindRefundByKey("other"));
    }

    [Fact]
    public void FailedRefundDoesNotConsumeBalance()
    {
        var payment = CapturedPayment(10m);
        payment.AddRefund("R1", 4m, "FAILED", "k1");
        Assert.Equal(10m, payment.RefundableRemaining);
        Assert.Equal(0m, payment.TotalRefunded);
    }
}
