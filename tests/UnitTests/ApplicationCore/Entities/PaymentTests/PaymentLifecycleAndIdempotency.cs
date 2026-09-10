using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycleAndIdempotency
{
    private const string BuyerId = "buyer@example.com";

    [Fact]
    public void AuthorizingRecordsTheHold()
    {
        var payment = new Payment(1, BuyerId, "USD", 12m);
        var expiry = DateTimeOffset.UtcNow.AddDays(3);

        payment.MarkAuthorized("AUTH1", "CREATED", expiry);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal(expiry, payment.AuthorizationExpiresAt);
    }

    [Fact]
    public void RenewingReplacesTheHoldWithoutChangingStatus()
    {
        var payment = new Payment(1, BuyerId, "USD", 12m);
        payment.MarkAuthorized("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1));

        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
        payment.RenewAuthorization("AUTH2", "CREATED", newExpiry);

        Assert.Equal("AUTH2", payment.AuthorizationId);
        Assert.Equal(newExpiry, payment.AuthorizationExpiresAt);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Fact]
    public void VoidingReleasesTheHold()
    {
        var payment = new Payment(1, BuyerId, "USD", 12m);
        payment.MarkAuthorized("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Fact]
    public void IdempotencyKeysAreDerivedFromTheRootAndAreStable()
    {
        var payment = new Payment(1, BuyerId, "USD", 12m);

        Assert.False(string.IsNullOrWhiteSpace(payment.IdempotencyRoot));
        Assert.Equal($"{payment.IdempotencyRoot}-order", payment.OrderKey);
        Assert.Equal($"{payment.IdempotencyRoot}-authorize", payment.AuthorizeKey);
        Assert.Equal($"{payment.IdempotencyRoot}-capture", payment.CaptureKey);

        // The keys are stable across reads for the same payment.
        Assert.Equal(payment.CaptureKey, payment.CaptureKey);
    }

    [Fact]
    public void TwoPaymentsGetDistinctIdempotencyRoots()
    {
        var a = new Payment(1, BuyerId, "USD", 12m);
        var b = new Payment(1, BuyerId, "USD", 12m);

        // Even for the same order id (e.g. after an in-memory restart), roots differ so PayPal keys never collide.
        Assert.NotEqual(a.IdempotencyRoot, b.IdempotencyRoot);
    }
}
