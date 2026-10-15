using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests;

public class SubscriptionClaimStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T10:00:00Z");

    [Fact]
    public async Task ASecondEnrollmentClaimForTheSameBuyerAndPlanIsRefused()
    {
        var harness = new BillingHarness();

        var first = await harness.NewStore().TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "plan", "ref-1", Now));
        var second = await harness.NewStore().TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "plan", "ref-2", Now));

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal("ref-1", second!.SubscriptionReference);
    }

    [Fact]
    public async Task ClaimsForDifferentPlansDoNotCollide()
    {
        var harness = new BillingHarness();

        Assert.Null(await harness.NewStore().TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "plan-a", "ref-a", Now)));
        Assert.Null(await harness.NewStore().TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "plan-b", "ref-b", Now)));
    }

    [Fact]
    public async Task ASecondCustomerClaimForTheSameBuyerIsRefused()
    {
        var harness = new BillingHarness();

        Assert.Null(await harness.NewStore().TryClaimCustomerAsync(new BillingCustomer("buyer", "eshop-1", Now)));
        var existing = await harness.NewStore().TryClaimCustomerAsync(new BillingCustomer("buyer", "eshop-1", Now));

        Assert.NotNull(existing);
    }

    [Fact]
    public async Task AConcurrentStateChangeLosesTheOptimisticConcurrencyCheck()
    {
        var harness = new BillingHarness();
        await harness.NewStore().TryClaimEnrollmentAsync(new SubscriptionEnrollment("buyer", "plan", "ref-1", Now));

        var storeA = harness.NewStore();
        var storeB = harness.NewStore();
        var copyA = Assert.Single(await storeA.ListEnrollmentsAsync("buyer"));
        var copyB = Assert.Single(await storeB.ListEnrollmentsAsync("buyer"));

        copyA.Reclaim("ref-a", Now.AddMinutes(10));
        copyB.Reclaim("ref-b", Now.AddMinutes(10));

        Assert.True(await storeA.TrySaveEnrollmentAsync(copyA));
        Assert.False(await storeB.TrySaveEnrollmentAsync(copyB));
    }
}
