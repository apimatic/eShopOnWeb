using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class BillingClaimStoreTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_dbName).Options);

    [Fact]
    public async Task SecondSubscriptionClaimOnAnotherContextIsRefusedByThePrimaryKey()
    {
        var first = await new BillingClaimStore(NewContext()).TryClaimSubscriptionAsync("u", "p", default);
        var second = await new BillingClaimStore(NewContext()).TryClaimSubscriptionAsync("u", "p", default);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.Equal(ClaimState.Claimed, second.Claim.State);
        Assert.Equal(first.Claim.ClaimedAtUtc, second.Claim.ClaimedAtUtc);
    }

    [Fact]
    public async Task SecondClaimOnTheSameContextIsRefusedToo()
    {
        var store = new BillingClaimStore(NewContext());

        var first = await store.TryClaimSubscriptionAsync("u", "p", default);
        var second = await store.TryClaimSubscriptionAsync("u", "p", default);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
    }

    [Fact]
    public async Task DifferentPlanOrUserIsNotARefusal()
    {
        var store = new BillingClaimStore(NewContext());

        Assert.True((await store.TryClaimSubscriptionAsync("u", "p1", default)).Acquired);
        Assert.True((await store.TryClaimSubscriptionAsync("u", "p2", default)).Acquired);
        Assert.True((await store.TryClaimSubscriptionAsync("v", "p1", default)).Acquired);
    }

    [Fact]
    public async Task CustomerClaimIsRefusedPerUser()
    {
        var first = await new BillingClaimStore(NewContext()).TryClaimCustomerAsync("u", default);
        var second = await new BillingClaimStore(NewContext()).TryClaimCustomerAsync("u", default);
        var other = await new BillingClaimStore(NewContext()).TryClaimCustomerAsync("v", default);

        Assert.True(first.Acquired);
        Assert.False(second.Acquired);
        Assert.True(other.Acquired);
    }

    [Fact]
    public async Task ReleaseOnlyRemovesTheClaimItWasGivenAndAllowsReclaim()
    {
        var store = new BillingClaimStore(NewContext());
        var claim = (await store.TryClaimSubscriptionAsync("u", "p", default)).Claim;

        await store.ReleaseSubscriptionAsync("u", "p", claim.ClaimedAtUtc.AddSeconds(1), default); // not ours
        Assert.False((await new BillingClaimStore(NewContext()).TryClaimSubscriptionAsync("u", "p", default)).Acquired);

        await store.ReleaseSubscriptionAsync("u", "p", claim.ClaimedAtUtc, default);
        Assert.True((await new BillingClaimStore(NewContext()).TryClaimSubscriptionAsync("u", "p", default)).Acquired);
    }

    [Fact]
    public async Task CompleteRecordsTheSubscriptionId()
    {
        var store = new BillingClaimStore(NewContext());
        await store.TryClaimSubscriptionAsync("u", "p", default);

        await store.CompleteSubscriptionAsync("u", "p", 42, default);

        var saved = await NewContext().SubscriptionClaims.SingleAsync();
        Assert.Equal(ClaimState.Completed, saved.State);
        Assert.Equal(42, saved.ProviderSubscriptionId);
    }
}
