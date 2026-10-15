using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.ClaimStoreTests;

/// <summary>
/// Two contexts over one store stand in for two concurrent requests: the store itself must refuse
/// the second claim, not a read made before the insert.
/// </summary>
public class EfClaimStoreTests
{
    private readonly DbContextOptions<CatalogContext> _options = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: "ClaimStore-" + Guid.NewGuid())
        .Options;

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SecondClaimForSameUserAndPlanIsRefused()
    {
        var first = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        var second = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));

        Assert.True(await first.TryAddAsync(new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-1", Now), CancellationToken.None));
        Assert.False(await second.TryAddAsync(new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-2", Now), CancellationToken.None));

        var stored = await second.FindAsync(new object[] { "USER", "eshop-pro" }, CancellationToken.None);
        Assert.Equal("eshop-sub-1", stored!.Reference);
    }

    [Fact]
    public async Task ClaimForAnotherPlanIsAccepted()
    {
        var store = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));

        Assert.True(await store.TryAddAsync(new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-1", Now), CancellationToken.None));
        Assert.True(await store.TryAddAsync(new SubscriptionClaim("USER", "basic-plan", "eshop-sub-2", Now), CancellationToken.None));
    }

    [Fact]
    public async Task SecondCustomerLinkForSameUserIsRefusedInSameContext()
    {
        var store = new EfClaimStore<BillingCustomerLink>(new CatalogContext(_options));

        Assert.True(await store.TryAddAsync(new BillingCustomerLink("USER", "eshop:user", Now), CancellationToken.None));
        Assert.False(await store.TryAddAsync(new BillingCustomerLink("USER", "eshop:user", Now), CancellationToken.None));
    }

    [Fact]
    public async Task OnlyOneWriterCanTakeOverAClaim()
    {
        var seed = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        await seed.TryAddAsync(new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-1", Now), CancellationToken.None);

        var storeA = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        var storeB = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        var claimA = await storeA.FindAsync(new object[] { "USER", "eshop-pro" }, CancellationToken.None);
        var claimB = await storeB.FindAsync(new object[] { "USER", "eshop-pro" }, CancellationToken.None);

        claimA!.MarkUnknown(Now.AddMinutes(1));
        claimB!.MarkUnknown(Now.AddMinutes(1));

        Assert.True(await storeA.TryUpdateAsync(claimA, CancellationToken.None));
        Assert.False(await storeB.TryUpdateAsync(claimB, CancellationToken.None));
    }

    [Fact]
    public async Task ReleasedClaimCanBeClaimedAgain()
    {
        var store = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        var claim = new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-1", Now);
        await store.TryAddAsync(claim, CancellationToken.None);

        await store.RemoveAsync(claim, CancellationToken.None);

        var other = new EfClaimStore<SubscriptionClaim>(new CatalogContext(_options));
        Assert.True(await other.TryAddAsync(new SubscriptionClaim("USER", "eshop-pro", "eshop-sub-2", Now), CancellationToken.None));
    }
}
