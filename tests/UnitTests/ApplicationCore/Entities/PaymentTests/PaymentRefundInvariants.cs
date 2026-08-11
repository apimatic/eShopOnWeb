using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundInvariants
{
    private static Payment AuthorizedAndCaptured(decimal amount)
    {
        var payment = new Payment(orderId: 1, buyerId: "shopper@example.com", amount: amount, currencyCode: "USD");
        payment.MarkAuthorized("PPORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), savedPaymentMethodId: null);
        payment.MarkCaptured("CAP-1", "COMPLETED", capturedAmount: amount, paypalFee: 1.00m, netAmount: amount - 1.00m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsAwaitingPaymentWithUniqueSeed()
    {
        var a = new Payment(1, "b@x.com", 10m, "USD");
        var b = new Payment(2, "b@x.com", 10m, "USD");

        Assert.Equal(PaymentStatus.AwaitingPayment, a.Status);
        Assert.NotEqual(Guid.Empty, a.IdempotencySeed);
        Assert.NotEqual(a.IdempotencySeed, b.IdempotencySeed);
    }

    [Fact]
    public void PartialRefundLeavesRemainingAndPartiallyRefundedStatus()
    {
        var payment = AuthorizedAndCaptured(32.50m);

        payment.AddRefund("REF-1", 10m, "key-a", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.TotalRefunded);
        Assert.Equal(22.50m, payment.RefundableRemaining);
    }

    [Fact]
    public void TwoDistinctPartialRefundsAccumulate()
    {
        var payment = AuthorizedAndCaptured(30m);

        payment.AddRefund("REF-1", 10m, "key-a", "COMPLETED");
        payment.AddRefund("REF-2", 5m, "key-b", "COMPLETED");

        Assert.Equal(15m, payment.TotalRefunded);
        Assert.Equal(15m, payment.RefundableRemaining);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void RefundingFullRemainingMarksRefunded()
    {
        var payment = AuthorizedAndCaptured(20m);

        payment.AddRefund("REF-1", 20m, "key-a", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public void FailedRefundDoesNotReduceRefundableRemaining()
    {
        var payment = AuthorizedAndCaptured(20m);

        payment.AddRefund("REF-1", 20m, "key-a", "FAILED");

        // A failed refund never moved money, so the full amount is still refundable.
        Assert.Equal(20m, payment.RefundableRemaining);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsTheMatchingRefund()
    {
        var payment = AuthorizedAndCaptured(20m);
        payment.AddRefund("REF-1", 5m, "key-a", "COMPLETED");

        Assert.NotNull(payment.FindRefundByIdempotencyKey("key-a"));
        Assert.Null(payment.FindRefundByIdempotencyKey("key-unknown"));
    }
}

public class PaymentReferenceTests
{
    [Fact]
    public void RoundTripsOrderIdThroughInvoiceReference()
    {
        var seed = Guid.NewGuid();
        var reference = PaymentReference.For(123, seed);

        Assert.StartsWith("ESHOP-ORDER-123-", reference);
        Assert.Equal(123, PaymentReference.TryGetOrderId(reference));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOME-OTHER-INVOICE")]
    [InlineData("ESHOP-ORDER-notanumber-abc")]
    public void ReturnsNullForForeignOrMalformedInvoices(string? invoiceId)
    {
        Assert.Null(PaymentReference.TryGetOrderId(invoiceId));
    }
}
