using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentTests
{
    private static Payment CreateCapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@example.com", amount: amount, currency: "USD");
        payment.SetPayPalOrderId("PAYPAL-ORDER-1");
        payment.MarkAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);
        payment.MarkCaptured("CAPTURE-1", amount, 3.20m, amount - 3.20m);
        return payment;
    }

    [Fact]
    public void NewPaymentStartsPendingAuthorization()
    {
        var payment = new Payment(1, "buyer@example.com", 10m, "USD");
        Assert.Equal(PaymentStatus.PendingAuthorization, payment.Status);
        Assert.Equal(0m, payment.TotalRefunded);
    }

    [Fact]
    public void PartialRefundLeavesRefundableBalance()
    {
        var payment = CreateCapturedPayment();

        payment.AddRefund("REF-1", 40m, "key-1", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded);
        Assert.Equal(60m, payment.RefundableAmount);
    }

    [Fact]
    public void FullRefundMarksPaymentRefunded()
    {
        var payment = CreateCapturedPayment();

        payment.AddRefund("REF-1", 100m, "key-1", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var payment = CreateCapturedPayment();
        payment.AddRefund("REF-1", 60m, "key-1", "COMPLETED");

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("REF-2", 41m, "key-2", "COMPLETED"));
        Assert.Equal(60m, payment.TotalRefunded);
    }

    [Fact]
    public void DistinctPartialRefundsAccumulate()
    {
        var payment = CreateCapturedPayment();

        payment.AddRefund("REF-1", 25m, "key-1", "COMPLETED");
        payment.AddRefund("REF-2", 25m, "key-2", "COMPLETED");

        Assert.Equal(50m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }
}
