using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefunds
{
    private static Payment CapturedPayment(decimal amount = 100m)
    {
        var payment = new Payment(1, "buyer@test", "USD", amount);
        payment.MarkAuthorized("AUTH-1", "CREATED");
        payment.MarkCaptured("CAP-1", "COMPLETED", amount, 3m, amount - 3m);
        return payment;
    }

    [Fact]
    public void PartialRefund_TransitionsToPartiallyRefunded_AndTracksRemaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("key-1", 40m, "RF-1", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(40m, payment.TotalRefunded());
        Assert.Equal(60m, payment.RefundableRemaining());
    }

    [Fact]
    public void TwoDistinctPartialRefunds_AreBothRecorded()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("key-1", 40m, "RF-1", "COMPLETED");
        payment.AddRefund("key-2", 25m, "RF-2", "COMPLETED");

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(65m, payment.TotalRefunded());
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
    }

    [Fact]
    public void RefundingTheFullRemainder_TransitionsToRefunded()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 40m, "RF-1", "COMPLETED");

        payment.AddRefund("key-2", 60m, "RF-2", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void RefundExceedingRemaining_Throws_AndNeverBecomesRefundableBeyondCaptured()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 80m, "RF-1", "COMPLETED");

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-2", 30m, "RF-2", "COMPLETED"));
        Assert.Equal(80m, payment.TotalRefunded());
        Assert.Equal(20m, payment.RefundableRemaining());
    }

    [Fact]
    public void RefundBeforeCapture_Throws()
    {
        var payment = new Payment(1, "buyer@test", "USD", 100m);
        payment.MarkAuthorized("AUTH-1", "CREATED");

        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("key-1", 10m, "RF-1", "COMPLETED"));
    }

    [Fact]
    public void TryGetRefundByKey_FindsExistingRefund()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("key-1", 40m, "RF-1", "COMPLETED");

        var found = payment.TryGetRefundByKey("key-1", out var refund);

        Assert.True(found);
        Assert.NotNull(refund);
        Assert.Equal("RF-1", refund!.PayPalRefundId);
        Assert.False(payment.TryGetRefundByKey("missing", out _));
    }
}
