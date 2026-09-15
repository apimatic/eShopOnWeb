using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment CapturedPayment()
    {
        var p = new Payment(1, "buyer@x.com", 50m, "USD", "ESHOP-1-abc");
        p.RecordAuthorization("O1", "A1", "CREATED", null, null);
        p.RecordCapture("C1", "COMPLETED", 50m, 2m, 48m);
        return p;
    }

    [Fact]
    public void AuthorizeIdempotencyKeyIsStableAcrossCalls()
    {
        var p = new Payment(1, "b", 10m, "USD", "inv");
        var first = p.EnsureAuthorizeIdempotencyKey();
        var second = p.EnsureAuthorizeIdempotencyKey();
        Assert.Equal(first, second);
    }

    [Fact]
    public void PartialRefundLeavesRemainingAndMarksPartiallyRefunded()
    {
        var p = CapturedPayment();
        p.AddRefund("R1", 20m, "COMPLETED", "k1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(20m, p.RefundedAmount());
        Assert.Equal(30m, p.RefundableRemaining());
    }

    [Fact]
    public void RefundsUpToCaptureMarkRefundedAndCannotExceedCapture()
    {
        var p = CapturedPayment();
        p.AddRefund("R1", 30m, "COMPLETED", "k1");
        p.AddRefund("R2", 20m, "COMPLETED", "k2");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());   // never negative / beyond captured
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsPriorRefund()
    {
        var p = CapturedPayment();
        p.AddRefund("R1", 5m, "COMPLETED", "dup-key");

        Assert.NotNull(p.FindRefundByIdempotencyKey("dup-key"));
        Assert.Null(p.FindRefundByIdempotencyKey("other"));
    }
}
