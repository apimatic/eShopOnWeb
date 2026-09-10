using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundRules
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer1", currencyCode: "USD",
            amount: amount, invoiceReference: "eshop-run-1");
        payment.RecordAuthorization("PPORDER", "AUTH1", "CREATED", null);
        payment.RecordCapture("CAP1", "COMPLETED", amount, payPalFee: 3m, netAmount: amount - 3m);
        return payment;
    }

    [Fact]
    public void CaptureRecordsFeeAndNetProceeds()
    {
        var payment = CapturedPayment(100m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3m, payment.PayPalFee);
        Assert.Equal(97m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableRemaining);
    }

    [Fact]
    public void PartialRefundReducesRefundableRemaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF1", 40m, "COMPLETED", "keyA");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded);
        Assert.Equal(60m, payment.RefundableRemaining);
    }

    [Fact]
    public void FullyRefundedWhenNothingRemains()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("REF1", 60m, "COMPLETED", "keyA");
        payment.AddRefund("REF2", 40m, "COMPLETED", "keyB");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void CannotRefundBeyondCapturedAmount()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("REF1", 70m, "COMPLETED", "keyA");

        // Only 30 remains; a 40 refund must be rejected.
        Assert.Throws<PaymentOperationException>(() => payment.EnsureRefundable(40m));
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new Payment(1, "buyer1", "USD", 50m, "eshop-run-1");
        payment.RecordAuthorization("PPORDER", "AUTH1", "CREATED", null);

        Assert.Throws<PaymentOperationException>(() => payment.EnsureRefundable(10m));
    }

    [Fact]
    public void FindRefundByKeyReturnsExistingRefundForSameKey()
    {
        var payment = CapturedPayment(100m);
        var first = payment.AddRefund("REF1", 10m, "COMPLETED", "dup-key");

        var found = payment.FindRefundByKey("dup-key");

        Assert.NotNull(found);
        Assert.Equal(first.RefundId, found!.RefundId);
        Assert.Null(payment.FindRefundByKey("other-key"));
    }

    [Fact]
    public void CancelledRefundsDoNotCountTowardTotalRefunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("REF1", 30m, "CANCELLED", "keyA");
        payment.AddRefund("REF2", 20m, "COMPLETED", "keyB");

        // The cancelled refund is not counted, so 80 is still refundable.
        Assert.Equal(20m, payment.TotalRefunded);
        Assert.Equal(80m, payment.RefundableRemaining);
    }
}
