using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentRefundGuards
{
    private static Payment AuthorizedPayment(decimal amount = 50m) =>
        new Payment("PPO-1", "AUTH-1", "CREATED", amount, "USD");

    private static Payment CapturedPayment(decimal captured = 50m)
    {
        var payment = AuthorizedPayment(captured);
        payment.RecordCapture("CAP-1", "COMPLETED", captured, 2m, captured - 2m);
        return payment;
    }

    [Fact]
    public void RefundableEqualsCapturedBeforeAnyRefund()
    {
        var payment = CapturedPayment(50m);
        Assert.Equal(50m, payment.RefundableAmount);
        Assert.Equal(0m, payment.RefundedAmount);
    }

    [Fact]
    public void PartialRefundsAccumulateAndReduceRefundable()
    {
        var payment = CapturedPayment(50m);

        payment.AddRefund("R-1", 20m, "COMPLETED", "key-1");
        payment.AddRefund("R-2", 15m, "COMPLETED", "key-2");

        Assert.Equal(35m, payment.RefundedAmount);
        Assert.Equal(15m, payment.RefundableAmount);
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund("R-1", 40m, "COMPLETED", "key-1");

        // Only 10 remains; a further 20 must be refused so the order never becomes refundable beyond capture.
        var ex = Assert.Throws<InvalidOperationException>(() => payment.AddRefund("R-2", 20m, "COMPLETED", "key-2"));
        Assert.Contains("exceeds", ex.Message);
        Assert.Equal(40m, payment.RefundedAmount);
    }

    [Fact]
    public void RefundBeforeCaptureIsRejected()
    {
        var payment = AuthorizedPayment(50m);
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund("R-1", 5m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void FindRefundByKeyReturnsTheExistingRefund()
    {
        var payment = CapturedPayment(50m);
        payment.AddRefund("R-1", 10m, "COMPLETED", "the-key");

        var found = payment.FindRefundByKey("the-key");

        Assert.NotNull(found);
        Assert.Equal("R-1", found!.RefundId);
        Assert.Null(payment.FindRefundByKey("other-key"));
    }

    [Fact]
    public void RenewAuthorizationReplacesTheAuthorizationId()
    {
        var payment = AuthorizedPayment(50m);
        payment.RenewAuthorization("AUTH-2", "CREATED");

        Assert.Equal("AUTH-2", payment.AuthorizationId);
    }
}
