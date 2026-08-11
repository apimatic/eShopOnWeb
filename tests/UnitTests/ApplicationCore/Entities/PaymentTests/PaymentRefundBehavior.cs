using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundBehavior
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(1, "buyer", amount, "USD");
        payment.MarkAuthorized("O1", "A1", "CREATED", null, null);
        payment.MarkCaptured("C1", "COMPLETED", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void PartialRefund_LeavesPaymentPartiallyRefunded_AndReducesRefundable()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("R1", 30m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(30m, payment.RefundedAmount());
        Assert.Equal(70m, payment.RefundableAmount());
    }

    [Fact]
    public void MultiplePartialRefunds_CannotExceedCaptured()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("R2", 60m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void FindRefundByKey_ReturnsExistingRefund()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R1", 10m, "COMPLETED", "key-1");

        Assert.NotNull(payment.FindRefundByKey("key-1"));
        Assert.Null(payment.FindRefundByKey("key-2"));
    }

    [Fact]
    public void FailedRefund_DoesNotCountAgainstRefundable()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R1", 25m, "FAILED", "key-1");

        Assert.Equal(0m, payment.RefundedAmount());
        Assert.Equal(100m, payment.RefundableAmount());
    }
}
