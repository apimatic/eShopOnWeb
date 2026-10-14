using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.BillingClaimStoreTests;

/// <summary>
/// Proves the store refuses a second claim for the same key when it arrives through a different
/// DbContext (i.e. a different request), which is what stops a double-click before it reaches the provider.
/// </summary>
public class ClaimRefusal
{
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"BillingClaims-{Guid.NewGuid()}")
        .Options;

    [Fact]
    public async Task SecondCustomerClaimForSameBuyerIsRefused()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var second = new EfBillingClaimStore(new CatalogContext(_dbOptions));

        Assert.True(await first.TryClaimAsync(new BillingCustomer("buyer@example.com", "eshop:buyer@example.com", now)));
        Assert.False(await second.TryClaimAsync(new BillingCustomer("buyer@example.com", "eshop:buyer@example.com", now)));
    }

    [Fact]
    public async Task SecondEnrollmentClaimForSameReferenceIsRefused()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var second = new EfBillingClaimStore(new CatalogContext(_dbOptions));

        Assert.True(await first.TryClaimAsync(new SubscriptionEnrollment("ref-1", "buyer@example.com", "eshop-pro", now)));
        Assert.False(await second.TryClaimAsync(new SubscriptionEnrollment("ref-1", "buyer@example.com", "eshop-pro", now)));
    }

    [Fact]
    public async Task RefusedClaimInAContextThatAlreadyReadTheHeldRowLeavesNothingBehindToSave()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(await new EfBillingClaimStore(new CatalogContext(_dbOptions)).TryClaimAsync(new BillingCustomer("buyer@example.com", "eshop:buyer@example.com", now)));

        var store = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var held = (await store.GetCustomerAsync("buyer@example.com"))!;

        Assert.False(await store.TryClaimAsync(new BillingCustomer("buyer@example.com", "eshop:buyer@example.com", now)));

        held.MarkLinked(42, now);
        Assert.True(await store.TryUpdateAsync(held));
        Assert.Equal(42, (await new EfBillingClaimStore(new CatalogContext(_dbOptions)).GetCustomerAsync("buyer@example.com"))!.ProviderCustomerId);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentTakeOversSucceeds()
    {
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var seed = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        Assert.True(await seed.TryClaimAsync(new SubscriptionEnrollment("ref-2", "buyer@example.com", "eshop-pro", claimedAt)));

        var firstStore = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var secondStore = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var firstCopy = (await firstStore.GetEnrollmentAsync("ref-2"))!;
        var secondCopy = (await secondStore.GetEnrollmentAsync("ref-2"))!;

        firstCopy.TakeOver(DateTimeOffset.UtcNow);
        secondCopy.TakeOver(DateTimeOffset.UtcNow);

        Assert.True(await firstStore.TryUpdateAsync(firstCopy));
        Assert.False(await secondStore.TryUpdateAsync(secondCopy));
    }

    [Fact]
    public async Task ReleasedClaimCanBeTakenAgain()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        var claim = new SubscriptionEnrollment("ref-3", "buyer@example.com", "eshop-pro", now);
        Assert.True(await store.TryClaimAsync(claim));

        await store.ReleaseAsync(claim);

        var other = new EfBillingClaimStore(new CatalogContext(_dbOptions));
        Assert.True(await other.TryClaimAsync(new SubscriptionEnrollment("ref-3", "buyer@example.com", "eshop-pro", now)));
    }
}
