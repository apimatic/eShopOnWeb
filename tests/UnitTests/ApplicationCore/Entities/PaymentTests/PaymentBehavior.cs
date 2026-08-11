using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment CapturedPayment(decimal gross = 20m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@test", amount: gross, currency: "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", gross, fee: 1m, net: gross - 1m);
        return payment;
    }

    [Fact]
    public void NewPaymentAwaitsPayment()
    {
        var payment = new Payment(1, "buyer@test", 20m, "USD");

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.Equal(20m, payment.Amount);
    }

    [Fact]
    public void AuthorizingMovesToAuthorized()
    {
        var payment = new Payment(1, "buyer@test", 20m, "USD");

        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal("PPORDER", payment.PayPalOrderId);
    }

    [Fact]
    public void CapturingRecordsGrossFeeAndNet()
    {
        var payment = CapturedPayment(20m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(20m, payment.CapturedGross);
        Assert.Equal(1m, payment.PayPalFee);
        Assert.Equal(19m, payment.NetAmount);
        Assert.Equal(20m, payment.RefundableRemaining);
    }

    [Fact]
    public void PartialRefundReducesRefundableRemaining()
    {
        var payment = CapturedPayment(20m);

        payment.AddRefund(new PaymentRefund("k1", "R1", 5m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(5m, payment.TotalRefunded);
        Assert.Equal(15m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundingFullAmountMovesToRefunded()
    {
        var payment = CapturedPayment(20m);

        payment.AddRefund(new PaymentRefund("k1", "R1", 12m, "USD", "COMPLETED"));
        payment.AddRefund(new PaymentRefund("k2", "R2", 8m, "USD", "COMPLETED"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(20m, payment.TotalRefunded);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FailedRefundDoesNotCountTowardTotal()
    {
        var payment = CapturedPayment(20m);

        payment.AddRefund(new PaymentRefund("k1", "R1", 5m, "USD", "FAILED"));

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(20m, payment.RefundableRemaining);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var payment = CapturedPayment(20m);
        payment.AddRefund(new PaymentRefund("dup-key", "R1", 5m, "USD", "COMPLETED"));

        var found = payment.FindRefundByIdempotencyKey("dup-key");

        Assert.NotNull(found);
        Assert.Equal("R1", found!.RefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CancellingMovesToCancelled()
    {
        var payment = new Payment(1, "buyer@test", 20m, "USD");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }
}
