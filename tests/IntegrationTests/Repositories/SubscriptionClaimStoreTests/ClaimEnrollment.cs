using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.SubscriptionClaimStoreTests;

public class ClaimEnrollment
{
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: "ClaimStore-" + Guid.NewGuid())
        .Options;

    [Fact]
    public async Task SecondClaimForSameBuyerAndPlan_IsRefused()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new EfSubscriptionClaimStore(new CatalogContext(_dbOptions));
        var second = new EfSubscriptionClaimStore(new CatalogContext(_dbOptions));

        Assert.True(await first.TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "eshop-pro", "ref-1", now)));
        Assert.False(await second.TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "eshop-pro", "ref-2", now)));
        Assert.True(await second.TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "basic-plan", "ref-3", now)));
    }

    [Fact]
    public async Task ConcurrentClaims_ExactlyOneWins()
    {
        var now = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            new EfSubscriptionClaimStore(new CatalogContext(_dbOptions))
                .TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "eshop-pro", $"ref-{i}", now)))));

        Assert.Equal(1, results.Count(won => won));
        await using var verify = new CatalogContext(_dbOptions);
        Assert.Equal(1, await verify.SubscriptionEnrollments.CountAsync());
    }

    [Fact]
    public async Task ReleasedClaim_CanBeClaimedAgain_AndDoubleReleaseIsHarmless()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new EfSubscriptionClaimStore(new CatalogContext(_dbOptions));
        var claim = new SubscriptionEnrollment("buyer", "eshop-pro", "ref-1", now);
        Assert.True(await store.TryClaimEnrollmentAsync(claim));

        await store.ReleaseEnrollmentAsync(claim);
        await new EfSubscriptionClaimStore(new CatalogContext(_dbOptions)).ReleaseEnrollmentAsync(new SubscriptionEnrollment("buyer", "eshop-pro", "ref-1", now));

        Assert.True(await store.TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "eshop-pro", "ref-2", now)));
    }

    [Fact]
    public async Task SecondCustomerClaimForSameBuyer_IsRefused()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new EfSubscriptionClaimStore(new CatalogContext(_dbOptions));
        var second = new EfSubscriptionClaimStore(new CatalogContext(_dbOptions));

        Assert.True(await first.TryClaimCustomerAsync(new BillingCustomer("buyer", now)));
        Assert.False(await second.TryClaimCustomerAsync(new BillingCustomer("buyer", now)));
    }
}
