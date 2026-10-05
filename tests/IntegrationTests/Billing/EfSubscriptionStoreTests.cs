using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>Two contexts over one in-memory database stand in for two concurrent requests.</summary>
public class EfSubscriptionStoreTests
{
    private readonly DbContextOptions<CatalogContext> _options = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase($"SubscriptionClaims-{Guid.NewGuid()}")
        .Options;

    private static readonly DateTimeOffset s_now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SecondEnrollmentClaimForSamePlanIsRefused()
    {
        using var first = new CatalogContext(_options);
        using var second = new CatalogContext(_options);

        Assert.True(await new EfSubscriptionStore(first).TryClaimEnrollmentAsync(
            new SubscriptionEnrollment("user-1", "eshop-pro", s_now), default));
        Assert.False(await new EfSubscriptionStore(second).TryClaimEnrollmentAsync(
            new SubscriptionEnrollment("user-1", "eshop-pro", s_now), default));

        // The refused context is still usable afterwards.
        Assert.True(await new EfSubscriptionStore(second).TryClaimEnrollmentAsync(
            new SubscriptionEnrollment("user-1", "basic-plan", s_now), default));
    }

    [Fact]
    public async Task SecondCustomerClaimForSameUserIsRefused()
    {
        using var first = new CatalogContext(_options);
        using var second = new CatalogContext(_options);

        Assert.True(await new EfSubscriptionStore(first).TryClaimCustomerAsync(new BillingCustomer("user-1", s_now), default));
        Assert.False(await new EfSubscriptionStore(second).TryClaimCustomerAsync(new BillingCustomer("user-1", s_now), default));
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentTakeoversWins()
    {
        using (var seed = new CatalogContext(_options))
        {
            await new EfSubscriptionStore(seed).TryClaimEnrollmentAsync(
                new SubscriptionEnrollment("user-1", "eshop-pro", s_now.AddMinutes(-5)), default);
        }

        using var first = new CatalogContext(_options);
        using var second = new CatalogContext(_options);
        var firstStore = new EfSubscriptionStore(first);
        var secondStore = new EfSubscriptionStore(second);
        var id = SubscriptionEnrollment.KeyFor("user-1", "eshop-pro");
        var seenByFirst = await firstStore.GetEnrollmentAsync(id, default);
        var seenBySecond = await secondStore.GetEnrollmentAsync(id, default);

        Assert.True(await firstStore.TryRenewEnrollmentClaimAsync(seenByFirst!, s_now, default));
        Assert.False(await secondStore.TryRenewEnrollmentClaimAsync(seenBySecond!, s_now, default));
    }

    [Fact]
    public async Task ReleasedClaimCanBeClaimedAgain()
    {
        using var context = new CatalogContext(_options);
        var store = new EfSubscriptionStore(context);
        var claim = new SubscriptionEnrollment("user-1", "eshop-pro", s_now);
        await store.TryClaimEnrollmentAsync(claim, default);

        await store.ReleaseEnrollmentAsync(claim, default);

        Assert.True(await store.TryClaimEnrollmentAsync(new SubscriptionEnrollment("user-1", "eshop-pro", s_now), default));
    }
}
