using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class SubscriptionEnrollmentStoreTests
{
    private static CatalogContext NewContext(string dbName)
        => new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);

    private static SubscriptionEnrollment Row(string user, string plan = "plan-a")
        => new(user, plan, "c-" + user, "s-" + user, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Second_claim_for_the_same_user_is_refused_by_the_primary_key_across_contexts()
    {
        var db = Guid.NewGuid().ToString();
        var first = await new SubscriptionEnrollmentStore(NewContext(db)).TryClaimAsync(Row("u1"));
        var second = await new SubscriptionEnrollmentStore(NewContext(db)).TryClaimAsync(Row("u1", "plan-b"));

        Assert.True(first.Claimed);
        Assert.False(second.Claimed);
        Assert.Equal("plan-a", second.Enrollment.PlanHandle); // the earlier row is returned
    }

    [Fact]
    public async Task Second_claim_in_the_same_context_is_refused_too()
    {
        var store = new SubscriptionEnrollmentStore(NewContext(Guid.NewGuid().ToString()));
        Assert.True((await store.TryClaimAsync(Row("u1"))).Claimed);
        Assert.False((await store.TryClaimAsync(Row("u1"))).Claimed);
    }

    [Fact]
    public async Task Release_frees_the_claim_and_save_persists_changes()
    {
        var db = Guid.NewGuid().ToString();
        var store = new SubscriptionEnrollmentStore(NewContext(db));
        var claim = await store.TryClaimAsync(Row("u1"));
        claim.Enrollment.MarkActive(5, 6, "active", DateTimeOffset.UtcNow);
        await store.SaveAsync(claim.Enrollment);

        var reloaded = await new SubscriptionEnrollmentStore(NewContext(db)).GetAsync("u1");
        Assert.Equal(EnrollmentStatus.Active, reloaded!.Status);
        Assert.Equal(5, reloaded.MaxioSubscriptionId);

        await store.ReleaseAsync(claim.Enrollment);
        Assert.Null(await new SubscriptionEnrollmentStore(NewContext(db)).GetAsync("u1"));
        Assert.True((await store.TryClaimAsync(Row("u1"))).Claimed);
    }
}
