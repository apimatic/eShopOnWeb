using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.BillingClaimStoreTests;

public class ClaimLifecycle
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    // Each "request" gets its own context over the same store, as in the running app.
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"BillingClaims-{Guid.NewGuid()}")
        .Options;

    private BillingClaimStore NewStore() => new(new CatalogContext(_dbOptions));

    [Fact]
    public async Task SecondClaimForTheSameUserIsRefused()
    {
        Assert.True(await NewStore().TryInsertAsync(new BillingCustomerClaim("USER", "eshop:user", Now, Lease), CancellationToken.None));

        var second = NewStore();
        Assert.False(await second.TryInsertAsync(new BillingCustomerClaim("USER", "eshop:user", Now, Lease), CancellationToken.None));

        // The refused entity is detached, so the same context remains usable.
        var existing = await second.GetAsync<BillingCustomerClaim>("USER", CancellationToken.None);
        Assert.NotNull(existing);
        Assert.Equal(BillingClaimStatus.Pending, existing!.Status);
    }

    [Fact]
    public async Task OnlyOneRequestCanTakeOverAnExpiredClaim()
    {
        await NewStore().TryInsertAsync(new BillingSubscriptionClaim("USER", "pro", "eshop-sub:1", Now, Lease), CancellationToken.None);

        var storeA = NewStore();
        var storeB = NewStore();
        var seenByA = await storeA.GetAsync<BillingSubscriptionClaim>("USER", CancellationToken.None);
        var seenByB = await storeB.GetAsync<BillingSubscriptionClaim>("USER", CancellationToken.None);
        var later = Now + Lease + TimeSpan.FromSeconds(1);

        Assert.True(await storeA.TryUpdateAsync(seenByA!, c => c.RenewLease(later, Lease), CancellationToken.None));
        Assert.False(await storeB.TryUpdateAsync(seenByB!, c => c.RenewLease(later, Lease), CancellationToken.None));
    }

    [Fact]
    public async Task CompletedClaimRecordsTheProviderIdAndReleasedClaimCanBeRetaken()
    {
        var store = NewStore();
        var claim = new BillingCustomerClaim("USER", "eshop:user", Now, Lease);
        await store.TryInsertAsync(claim, CancellationToken.None);
        Assert.True(await store.TryUpdateAsync(claim, c => c.Complete(42, Now), CancellationToken.None));

        var completed = await NewStore().GetAsync<BillingCustomerClaim>("USER", CancellationToken.None);
        Assert.Equal(BillingClaimStatus.Completed, completed!.Status);
        Assert.Equal(42, completed.ProviderCustomerId);

        var other = NewStore();
        Assert.True(await other.TryReleaseAsync(completed, CancellationToken.None));
        Assert.True(await other.TryInsertAsync(new BillingCustomerClaim("USER", "eshop:user", Now, Lease), CancellationToken.None));
    }
}
