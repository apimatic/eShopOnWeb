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

    // Each store gets its own context, like two concurrent HTTP requests would.
    private EfSubscriptionEnrollmentStore NewStore() => new(new CatalogContext(_dbOptions));

    private static SubscriptionEnrollment NewClaim(string user = "demouser@microsoft.com") =>
        SubscriptionEnrollment.Claim(user, "eshop-pro", "eshop-sub-" + user, DateTimeOffset.UtcNow);

    [Fact]
    public async Task SecondClaimForSameUserIsRefused()
    {
        Assert.True(await NewStore().TryClaimAsync(NewClaim(), CancellationToken.None));
        Assert.False(await NewStore().TryClaimAsync(NewClaim(), CancellationToken.None));
    }

    [Fact]
    public async Task SecondClaimInSameContextIsRefused()
    {
        var store = NewStore();
        Assert.True(await store.TryClaimAsync(NewClaim(), CancellationToken.None));
        Assert.False(await store.TryClaimAsync(NewClaim(), CancellationToken.None));
    }

    [Fact]
    public async Task ClaimsForDifferentUsersDoNotConflict()
    {
        Assert.True(await NewStore().TryClaimAsync(NewClaim("a@x.com"), CancellationToken.None));
        Assert.True(await NewStore().TryClaimAsync(NewClaim("b@x.com"), CancellationToken.None));
    }

    [Fact]
    public async Task ReleasedClaimCanBeTakenAgain()
    {
        var store = NewStore();
        var claim = NewClaim();
        await store.TryClaimAsync(claim, CancellationToken.None);

        await store.ReleaseAsync(claim, CancellationToken.None);

        Assert.Null(await NewStore().FindAsync(claim.UserName, CancellationToken.None));
        Assert.True(await NewStore().TryClaimAsync(NewClaim(), CancellationToken.None));
    }

    [Fact]
    public async Task SavePersistsChanges()
    {
        var claim = NewClaim();
        await NewStore().TryClaimAsync(claim, CancellationToken.None);

        claim.MarkActive(11, 22, "eshop-pro", DateTimeOffset.UtcNow);
        Assert.True(await NewStore().TrySaveAsync(claim, CancellationToken.None));

        var stored = await NewStore().FindAsync(claim.UserName, CancellationToken.None);
        Assert.Equal(EnrollmentStatus.Enrolled, stored!.Status);
        Assert.Equal(22, stored.BillingSubscriptionId);
    }

    [Fact]
    public async Task ConcurrentTakeOverOfSameRowLetsOnlyOneWin()
    {
        var claim = NewClaim();
        await NewStore().TryClaimAsync(claim, CancellationToken.None);
        claim.MarkOutcomeUnknown(DateTimeOffset.UtcNow);
        await NewStore().TrySaveAsync(claim, CancellationToken.None);

        var first = await NewStore().FindAsync(claim.UserName, CancellationToken.None);
        var second = await NewStore().FindAsync(claim.UserName, CancellationToken.None);
        first!.Restart("eshop-pro", DateTimeOffset.UtcNow);
        second!.Restart("basic-plan", DateTimeOffset.UtcNow);

        Assert.True(await NewStore().TrySaveAsync(first, CancellationToken.None));
        Assert.False(await NewStore().TrySaveAsync(second, CancellationToken.None));
        Assert.Equal("eshop-pro", (await NewStore().FindAsync(claim.UserName, CancellationToken.None))!.PlanHandle);
    }
}
