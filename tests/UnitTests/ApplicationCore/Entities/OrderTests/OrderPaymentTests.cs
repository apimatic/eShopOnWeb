using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    private static OrderPayment CapturedPayment(decimal amount = 100m)
    {
        var p = new OrderPayment(amount, "USD");
        p.SetAuthorization("PPORDER", "AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        p.SetCapture("CAP1", "COMPLETED", amount, 3.20m, amount - 3.20m);
        return p;
    }

    [Fact]
    public void RemainingRefundable_StartsAtCapturedGross()
    {
        var p = CapturedPayment(100m);
        Assert.Equal(100m, p.RemainingRefundable());
        Assert.Equal(0m, p.TotalRefunded());
    }

    [Fact]
    public void Refunds_ReduceRemainingRefundable_AndAccumulate()
    {
        var p = CapturedPayment(100m);
        p.AddRefund("k1", 30m, "R1", "COMPLETED");
        p.AddRefund("k2", 20m, "R2", "COMPLETED");

        Assert.Equal(50m, p.TotalRefunded());
        Assert.Equal(50m, p.RemainingRefundable());
    }

    [Fact]
    public void FailedRefund_DoesNotCountTowardTotal()
    {
        var p = CapturedPayment(100m);
        p.AddRefund("k1", 40m, "R1", "COMPLETED");
        p.AddRefund("k2", 25m, null, "FAILED");

        Assert.Equal(40m, p.TotalRefunded());
        Assert.Equal(60m, p.RemainingRefundable());
    }

    [Fact]
    public void FindRefundByKey_ReturnsExistingRefund()
    {
        var p = CapturedPayment(100m);
        var r = p.AddRefund("dup-key", 10m, "R1", "COMPLETED");
        Assert.Same(r, p.FindRefundByKey("dup-key"));
        Assert.Null(p.FindRefundByKey("other"));
    }

    [Fact]
    public void RotateAuthorizationKey_ChangesKeyOnlyWhenNotYetAuthorized()
    {
        var p = new OrderPayment(50m, "USD");
        var original = p.AuthorizationIdempotencyKey;
        p.RotateAuthorizationKey();
        Assert.NotEqual(original, p.AuthorizationIdempotencyKey);

        p.SetAuthorization("O", "A", "CREATED", null);
        var afterAuth = p.AuthorizationIdempotencyKey;
        p.RotateAuthorizationKey(); // no-op once a hold exists
        Assert.Equal(afterAuth, p.AuthorizationIdempotencyKey);
    }
}
