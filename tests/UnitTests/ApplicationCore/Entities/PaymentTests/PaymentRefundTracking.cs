using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundTracking
{
    private static Payment NewCapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(1, "USD", amount, "PP-ORDER-1", "AUTH-1", "CREATED", "req-authorize", null);
        payment.MarkCaptured("CAPTURE-1", "COMPLETED", "req-capture", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void RemainingRefundableEqualsCapturedAmountBeforeAnyRefund()
    {
        var payment = NewCapturedPayment(100m);

        Assert.Equal(100m, payment.RemainingRefundable);
    }

    [Fact]
    public void RemainingRefundableDecreasesAsRefundsAreAdded()
    {
        var payment = NewCapturedPayment(100m);

        payment.AddRefund(new Refund("REFUND-1", 40m, "COMPLETED", "key-1"));

        Assert.Equal(60m, payment.RemainingRefundable);
        Assert.Equal(40m, payment.RefundedAmount);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExistingRefundForRepeatedKey()
    {
        var payment = NewCapturedPayment(100m);
        var refund = new Refund("REFUND-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund(refund);

        var found = payment.FindRefundByIdempotencyKey("key-1");

        Assert.Same(refund, found);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsNullForUnknownKey()
    {
        var payment = NewCapturedPayment(100m);
        payment.AddRefund(new Refund("REFUND-1", 40m, "COMPLETED", "key-1"));

        var found = payment.FindRefundByIdempotencyKey("key-2");

        Assert.Null(found);
    }

    [Fact]
    public void TwoDistinctPartialRefundsAreBothTracked()
    {
        var payment = NewCapturedPayment(100m);

        payment.AddRefund(new Refund("REFUND-1", 40m, "COMPLETED", "key-1"));
        payment.AddRefund(new Refund("REFUND-2", 30m, "COMPLETED", "key-2"));

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(70m, payment.RefundedAmount);
        Assert.Equal(30m, payment.RemainingRefundable);
    }
}
