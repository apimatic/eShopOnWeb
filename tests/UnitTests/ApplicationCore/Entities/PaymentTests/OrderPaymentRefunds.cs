using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentRefunds
{
    private static OrderPayment CapturedPayment(decimal captured = 100m)
    {
        var payment = new OrderPayment(orderId: 1, buyerId: "buyer@test", amount: captured, currencyCode: "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "VISA ****1111", savedCardId: null);
        payment.MarkCaptured("CAP-1", captured, payPalFee: 3m, netAmount: captured - 3m);
        return payment;
    }

    [Fact]
    public void PartialRefund_TransitionsToPartiallyRefunded_AndReducesRefundable()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("R-1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.RefundedAmount());
        Assert.Equal(60m, payment.RefundableAmount());
    }

    [Fact]
    public void FullRefund_TransitionsToRefunded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("R-1", 100m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void Refund_IsIdempotentOnKey()
    {
        var payment = CapturedPayment(100m);

        var first = payment.AddRefund("R-1", 40m, "COMPLETED", "key-1");
        var second = payment.AddRefund("R-2", 40m, "COMPLETED", "key-1"); // same key

        Assert.Same(first, second);
        Assert.Single(payment.Refunds);
        Assert.Equal(40m, payment.RefundedAmount());
    }

    [Fact]
    public void TwoDistinctKeys_ProduceTwoRefunds()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("R-1", 40m, "COMPLETED", "key-1");
        payment.AddRefund("R-2", 25m, "COMPLETED", "key-2");

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(65m, payment.RefundedAmount());
        Assert.Equal(35m, payment.RefundableAmount());
    }

    [Fact]
    public void Refund_BeyondCaptured_Throws()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R-1", 70m, "COMPLETED", "key-1");

        var ex = Assert.Throws<PaymentException>(() => payment.AddRefund("R-2", 40m, "COMPLETED", "key-2"));
        Assert.Contains("exceeds the refundable amount", ex.Message);
        // The rejected refund never took effect.
        Assert.Equal(70m, payment.RefundedAmount());
    }

    [Fact]
    public void EachPayment_GetsAUniqueIdempotencyToken()
    {
        var a = new OrderPayment(1, "b", 10m, "USD");
        var b = new OrderPayment(2, "b", 10m, "USD");

        Assert.False(string.IsNullOrEmpty(a.IdempotencyToken));
        Assert.NotEqual(a.IdempotencyToken, b.IdempotencyToken);
    }
}
