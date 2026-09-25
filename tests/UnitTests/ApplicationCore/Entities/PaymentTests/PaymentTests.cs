using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentTests
{
    private static Payment FulfilledPayment(decimal captured = 39.00m)
    {
        var payment = new Payment(orderId: 1, buyerId: "buyer@example.com", currencyCode: "USD", amount: captured);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.MarkFulfilled("CAP1", "COMPLETED", captured, 1.50m, captured - 1.50m);
        return payment;
    }

    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithUniqueKeys()
    {
        var a = new Payment(1, "b", "USD", 10m);
        var b = new Payment(2, "b", "USD", 10m);

        Assert.Equal(PaymentStatus.AwaitingPayment, a.Status);
        Assert.NotEqual(Guid.Empty, a.PublicId);
        Assert.NotEqual(a.PublicId, b.PublicId);
        Assert.NotEqual(a.AuthorizeRequestKey, b.AuthorizeRequestKey);
        Assert.StartsWith("ESHOP-", a.ReconciliationReference);
    }

    [Fact]
    public void MarkFulfilled_RecordsCaptureAndProceeds()
    {
        var payment = FulfilledPayment();

        Assert.Equal(PaymentStatus.Fulfilled, payment.Status);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(39.00m, payment.CapturedAmount);
        Assert.Equal(1.50m, payment.PayPalFee);
        Assert.Equal(37.50m, payment.NetAmount);
        Assert.Equal(39.00m, payment.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_ExceedingCaptured_Throws()
    {
        var payment = FulfilledPayment(39.00m);

        Assert.Throws<InvalidOperationException>(() =>
            payment.AddRefund("k1", "R1", 40.00m, "COMPLETED"));
    }

    [Fact]
    public void AddRefund_SameKey_IsIdempotent_NoDoubleRefund()
    {
        var payment = FulfilledPayment(39.00m);

        var first = payment.AddRefund("k1", "R1", 10.00m, "COMPLETED");
        var again = payment.AddRefund("k1", "R2", 10.00m, "COMPLETED");

        Assert.Same(first, again);
        Assert.Equal("R1", again.PayPalRefundId);
        Assert.Equal(10.00m, payment.TotalRefunded());
        Assert.Single(payment.Refunds);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void AddRefund_DistinctKeys_AccumulateAndCapAtCaptured()
    {
        var payment = FulfilledPayment(39.00m);

        payment.AddRefund("k1", "R1", 10.00m, "COMPLETED");
        payment.AddRefund("k2", "R2", 5.00m, "COMPLETED");

        Assert.Equal(15.00m, payment.TotalRefunded());
        Assert.Equal(24.00m, payment.RefundableRemaining());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);

        payment.AddRefund("k3", "R3", 24.00m, "COMPLETED");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("k4", "R4", 0.01m, "COMPLETED"));
    }

    [Fact]
    public void MarkCancelled_FromAuthorized_VoidsHold()
    {
        var payment = new Payment(1, "b", "USD", 10m);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void Refund_BeforeFulfilment_Throws()
    {
        var payment = new Payment(1, "b", "USD", 10m);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("k1", "R1", 1m, "COMPLETED"));
    }
}
