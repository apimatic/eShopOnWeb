using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundInvariants
{
    private const string BuyerId = "buyer@example.com";

    private static Payment CapturedPayment(decimal amount = 29.00m)
    {
        var payment = new Payment(orderId: 1, buyerId: BuyerId, currencyCode: "USD", amount: amount);
        payment.SetPayPalOrder("PPORDER1", "ESHOP-1-INV");
        payment.MarkAuthorized("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        return payment;
    }

    [Fact]
    public void NewPaymentAwaitsInCreatedState()
    {
        var payment = new Payment(1, BuyerId, "USD", 10m);
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Equal(0m, payment.TotalRefunded());
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void MarkCapturedRecordsPayPalFigures()
    {
        var payment = CapturedPayment();
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(29.00m, payment.CapturedGrossAmount);
        Assert.Equal(1.24m, payment.PayPalFee);
        Assert.Equal(27.76m, payment.NetAmount);
        Assert.Equal(29.00m, payment.RefundableAmount());
    }

    [Fact]
    public void PartialRefundReducesRefundableAndMarksPartiallyRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("REF1", 10.00m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10.00m, payment.TotalRefunded());
        Assert.Equal(19.00m, payment.RefundableAmount());
    }

    [Fact]
    public void TwoDistinctPartialRefundsAccumulate()
    {
        var payment = CapturedPayment();

        payment.AddRefund("REF1", 10.00m, "COMPLETED", "key-1");
        payment.AddRefund("REF2", 5.00m, "COMPLETED", "key-2");

        Assert.Equal(15.00m, payment.TotalRefunded());
        Assert.Equal(14.00m, payment.RefundableAmount());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void RefundingTheFullRemainderMarksRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("REF1", 29.00m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount());
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF1", 20.00m, "COMPLETED", "key-1");

        var ex = Assert.Throws<PaymentDomainException>(() =>
            payment.AddRefund("REF2", 15.00m, "COMPLETED", "key-2"));
        Assert.Contains("exceeds the refundable amount", ex.Message);

        // The rejected refund left the running total untouched.
        Assert.Equal(20.00m, payment.TotalRefunded());
        Assert.Equal(9.00m, payment.RefundableAmount());
    }

    [Fact]
    public void RefundingAnUncapturedPaymentIsRejected()
    {
        var payment = new Payment(1, BuyerId, "USD", 10m);
        payment.MarkAuthorized("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Throws<PaymentDomainException>(() =>
            payment.AddRefund("REF1", 1.00m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void ExistingRefundIsFoundByIdempotencyKey()
    {
        var payment = CapturedPayment();
        payment.AddRefund("REF1", 10.00m, "COMPLETED", "key-1");

        var found = payment.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(found);
        Assert.Equal("REF1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("no-such-key"));
    }

    [Fact]
    public void RefundableAmountNeverGoesNegative()
    {
        var payment = CapturedPayment(5.00m);
        payment.AddRefund("REF1", 5.00m, "COMPLETED", "key-1");
        Assert.Equal(0m, payment.RefundableAmount());
    }
}
