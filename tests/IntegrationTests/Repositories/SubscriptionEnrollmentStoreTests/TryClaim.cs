#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.SubscriptionEnrollmentStoreTests;

public class TryClaim
{
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"Enrollments-{Guid.NewGuid()}")
        .Options;

    private static SubscriptionEnrollment NewClaim(string reference = "sub-ref") =>
        new("eshop-user-abc", "family", "user@test", "eshop-pro", reference, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ConcurrentClaims_ExactlyOneWins()
    {
        var stores = Enumerable.Range(0, 8).Select(_ => new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions))).ToList();

        var results = await Task.WhenAll(stores.Select((store, i) => Task.Run(() => store.TryClaimAsync(NewClaim($"sub-ref-{i}")))));

        Assert.Single(results, r => r is null);
        var winners = results.Where(r => r is not null).Select(r => r!.SubscriptionReference).Distinct().ToList();
        Assert.Single(winners);
    }

    [Fact]
    public async Task SecondClaim_ReturnsExistingEnrollment()
    {
        var first = new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions));
        var second = new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions));

        Assert.Null(await first.TryClaimAsync(NewClaim("first")));
        var existing = await second.TryClaimAsync(NewClaim("second"));

        Assert.NotNull(existing);
        Assert.Equal("first", existing!.SubscriptionReference);
    }

    [Fact]
    public async Task StaleUpdate_IsRefusedByConcurrencyStamp()
    {
        Assert.Null(await new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions)).TryClaimAsync(NewClaim()));
        var storeA = new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions));
        var storeB = new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions));
        var a = await storeA.GetAsync("eshop-user-abc|family");
        var b = await storeB.GetAsync("eshop-user-abc|family");

        a!.Retake("basic-plan", DateTimeOffset.UtcNow);
        b!.Retake("eshop-pro", DateTimeOffset.UtcNow);

        Assert.True(await storeA.TryUpdateAsync(a));
        Assert.False(await storeB.TryUpdateAsync(b));
    }

    [Fact]
    public async Task ReleasedClaim_CanBeClaimedAgain()
    {
        var store = new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions));
        var claim = NewClaim("one");
        Assert.Null(await store.TryClaimAsync(claim));

        await store.ReleaseAsync(claim);

        Assert.Null(await new EfSubscriptionEnrollmentStore(new CatalogContext(_dbOptions)).TryClaimAsync(NewClaim("two")));
    }
}
