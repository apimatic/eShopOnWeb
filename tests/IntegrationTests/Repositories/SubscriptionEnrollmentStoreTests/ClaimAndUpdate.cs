using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.SubscriptionEnrollmentStoreTests;

public class ClaimAndUpdate
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private static SubscriptionEnrollment NewClaim() => new("family:eshop-abc", "eshop-abc", "eshop-abc-family", "pro", Now);

    [Fact]
    public async Task SecondClaimForSameKey_FromAnotherContext_IsRefused()
    {
        var first = new EfSubscriptionEnrollmentStore(NewContext());
        var second = new EfSubscriptionEnrollmentStore(NewContext());

        Assert.True(await first.TryClaimAsync(NewClaim(), default));
        Assert.False(await second.TryClaimAsync(NewClaim(), default));

        // The refused store can still read the winning claim.
        var existing = await second.GetAsync("family:eshop-abc", default);
        Assert.Equal(SubscriptionEnrollmentStatus.Pending, existing!.Status);
    }

    [Fact]
    public async Task ReleasedClaim_CanBeTakenAgain()
    {
        var first = new EfSubscriptionEnrollmentStore(NewContext());
        var claim = NewClaim();
        await first.TryClaimAsync(claim, default);
        await first.ReleaseAsync(claim, default);

        Assert.True(await new EfSubscriptionEnrollmentStore(NewContext()).TryClaimAsync(NewClaim(), default));
    }

    [Fact]
    public async Task ConcurrentTakeOver_OnlyOneUpdateWins()
    {
        await new EfSubscriptionEnrollmentStore(NewContext()).TryClaimAsync(NewClaim(), default);

        var storeA = new EfSubscriptionEnrollmentStore(NewContext());
        var storeB = new EfSubscriptionEnrollmentStore(NewContext());
        var a = await storeA.GetAsync("family:eshop-abc", default);
        var b = await storeB.GetAsync("family:eshop-abc", default);

        a!.Restart("pro", Now.AddMinutes(5));
        b!.Restart("pro", Now.AddMinutes(5));

        Assert.True(await storeA.TryUpdateAsync(a, default));
        Assert.False(await storeB.TryUpdateAsync(b, default));
    }
}
