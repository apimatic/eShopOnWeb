using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentStateTransitions
{
    [Fact]
    public void MarkCapturedThrowsWhenNotAuthorized()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");

        Assert.Throws<PaymentStateConflictException>(
            () => payment.MarkCaptured("CAP-1", 10m, 0.5m, 9.5m, "COMPLETED"));
    }

    [Fact]
    public void MarkAuthorizedThrowsWhenAlreadyAuthorized()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", null);

        Assert.Throws<PaymentStateConflictException>(
            () => payment.MarkAuthorized("PPO-2", "AUTH-2", "CREATED", null));
    }

    [Fact]
    public void ResetForRetryClearsProviderStateAndRotatesKeys()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");
        var originalCreateKey = payment.CreateRequestKey;
        payment.MarkDeclined("card declined");

        payment.ResetForRetry();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.AuthorizationId);
        Assert.Null(payment.DeclineReason);
        Assert.NotEqual(originalCreateKey, payment.CreateRequestKey);
    }

    [Fact]
    public void ResetForRetryThrowsWhenAuthorized()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", null);

        Assert.Throws<PaymentStateConflictException>(() => payment.ResetForRetry());
    }

    [Fact]
    public void MarkVoidedThrowsWhenCaptured()
    {
        var payment = new Payment(1, "buyer", 10m, "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", null);
        payment.MarkCaptured("CAP-1", 10m, 0.5m, 9.5m, "COMPLETED");

        Assert.Throws<PaymentStateConflictException>(() => payment.MarkVoided());
    }
}
