using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.SubscriptionEnrollmentStoreTests;

public class ClaimTests
{
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"Enrollments-{Guid.NewGuid()}")
        .Options;

    // Each request gets its own DbContext, exactly like two concurrent HTTP requests.
    private EfSubscriptionEnrollmentStore NewStore() => new(new CatalogContext(_dbOptions), TimeProvider.System);

    [Fact]
    public async Task FirstClaimWins()
    {
        var claim = SubscriptionEnrollment.Claim("buyer@example.com", "eshop-pro", DateTimeOffset.UtcNow);

        var existing = await NewStore().TryClaimAsync(claim, CancellationToken.None);

        Assert.Null(existing);
    }

    [Fact]
    public async Task SecondClaimForSameBuyerIsRefusedAndReturnsTheWinner()
    {
        var first = SubscriptionEnrollment.Claim("buyer@example.com", "eshop-pro", DateTimeOffset.UtcNow);
        var second = SubscriptionEnrollment.Claim("buyer@example.com", "eshop-pro", DateTimeOffset.UtcNow);

        Assert.Null(await NewStore().TryClaimAsync(first, CancellationToken.None));
        var existing = await NewStore().TryClaimAsync(second, CancellationToken.None);

        Assert.NotNull(existing);
        Assert.Equal(first.SubscriptionReference, existing!.SubscriptionReference);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentTakeOversSucceeds()
    {
        var stale = SubscriptionEnrollment.Claim("buyer@example.com", "eshop-pro", DateTimeOffset.UtcNow.AddHours(-1));
        Assert.Null(await NewStore().TryClaimAsync(stale, CancellationToken.None));

        var storeA = NewStore();
        var storeB = NewStore();
        var seenByA = await storeA.GetAsync("buyer@example.com", CancellationToken.None);
        var seenByB = await storeB.GetAsync("buyer@example.com", CancellationToken.None);

        Assert.True(await storeA.TryTakeOverAsync(seenByA!, "eshop-pro", CancellationToken.None));
        Assert.False(await storeB.TryTakeOverAsync(seenByB!, "eshop-pro", CancellationToken.None));
    }

    [Fact]
    public async Task ReleasedClaimCanBeClaimedAgain()
    {
        var store = NewStore();
        var first = SubscriptionEnrollment.Claim("buyer@example.com", "eshop-pro", DateTimeOffset.UtcNow);
        Assert.Null(await store.TryClaimAsync(first, CancellationToken.None));

        await store.ReleaseAsync(first, CancellationToken.None);

        var again = SubscriptionEnrollment.Claim("buyer@example.com", "basic-plan", DateTimeOffset.UtcNow);
        Assert.Null(await NewStore().TryClaimAsync(again, CancellationToken.None));
    }
}
