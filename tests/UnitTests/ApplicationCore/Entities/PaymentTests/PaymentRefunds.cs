using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefunds
{
    private static Payment CreateCapturedPayment(decimal capturedAmount = 10m)
    {
        var payment = new Payment(1, "buyer", capturedAmount, "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkCaptured("CAP-1", capturedAmount, 0.50m, capturedAmount - 0.50m, "COMPLETED");
        return payment;
    }

    [Fact]
    public void AddRefundThrowsWhenAmountExceedsRefundable()
    {
        var payment = CreateCapturedPayment(10m);
        payment.AddRefund("key-1", 6m);

        Assert.Throws<PaymentStateConflictException>(() => payment.AddRefund("key-2", 5m));
    }

    [Fact]
    public void AddRefundThrowsOnDuplicateIdempotencyKey()
    {
        var payment = CreateCapturedPayment(10m);
        payment.AddRefund("key-1", 4m);

        Assert.Throws<PaymentStateConflictException>(() => payment.AddRefund("key-1", 4m));
    }

    [Fact]
    public void AddRefundThrowsWhenPaymentNotCaptured()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");

        Assert.Throws<PaymentStateConflictException>(() => payment.AddRefund("key-1", 10m));
    }

    [Fact]
    public void ApplyRefundedStatusMarksPartialThenFull()
    {
        var payment = CreateCapturedPayment(10m);

        var first = payment.AddRefund("key-1", 4m);
        first.MarkSettled("REF-1", PaymentRefundStatus.Completed);
        payment.ApplyRefundedStatus();
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(6m, payment.RefundableAmount);

        var second = payment.AddRefund("key-2", 6m);
        second.MarkSettled("REF-2", PaymentRefundStatus.Completed);
        payment.ApplyRefundedStatus();
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void TotalRefundedExcludesFailedRefunds()
    {
        var payment = CreateCapturedPayment(10m);
        var failed = payment.AddRefund("key-1", 4m);
        failed.MarkFailed(null);

        Assert.Equal(0m, payment.TotalRefunded);
        Assert.Equal(10m, payment.RefundableAmount);
    }
}
