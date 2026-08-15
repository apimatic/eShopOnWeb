using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentInvariants
{
    private static Payment AuthorizedPayment(decimal amount = 29m)
    {
        var payment = new Payment(1, "buyer@test.com", "USD", amount, "PPORDER1");
        payment.SetAuthorized("AUTH1", "CREATED");
        return payment;
    }

    private static Payment CapturedPayment(decimal amount = 29m)
    {
        var payment = AuthorizedPayment(amount);
        payment.SetCaptured("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return payment;
    }

    [Fact]
    public void SetAuthorized_MovesToAuthorized()
    {
        var payment = AuthorizedPayment();
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
    }

    [Fact]
    public void Capture_RecordsFeeAndNet()
    {
        var payment = CapturedPayment();
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(1.24m, payment.PayPalFee);
        Assert.Equal(27.76m, payment.NetAmount);
        Assert.Equal(29m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refund_BeyondCaptured_IsRejected()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new PaymentRefund("k1", "R1", 20m, "USD", "COMPLETED"));

        // 10 more would exceed the 9 remaining.
        Assert.Throws<PaymentDomainException>(() => payment.EnsureRefundable(10m));
        Assert.Equal(9m, payment.RefundableRemaining());
    }

    [Fact]
    public void TwoPartialRefunds_AccumulateAndCanFullyRefund()
    {
        var payment = CapturedPayment(29m);
        payment.AddRefund(new PaymentRefund("k1", "R1", 5m, "USD", "COMPLETED"));
        payment.AddRefund(new PaymentRefund("k2", "R2", 24m, "USD", "COMPLETED"));

        Assert.Equal(29m, payment.TotalRefunded());
        Assert.Equal(0m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExisting()
    {
        var payment = CapturedPayment();
        var refund = new PaymentRefund("dup-key", "R1", 5m, "USD", "COMPLETED");
        payment.AddRefund(refund);

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("dup-key"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void Void_OnlyFromAuthorized()
    {
        var captured = CapturedPayment();
        Assert.Throws<PaymentDomainException>(() => captured.Void());

        var authorized = AuthorizedPayment();
        authorized.Void();
        Assert.Equal(PaymentStatus.Voided, authorized.Status);
    }

    [Fact]
    public void EnsureRefundable_RequiresCapture()
    {
        var authorized = AuthorizedPayment();
        Assert.Throws<PaymentDomainException>(() => authorized.EnsureRefundable(1m));
    }
}
