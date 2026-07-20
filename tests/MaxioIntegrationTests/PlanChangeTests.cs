using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC3 plan-change preview + commit against the real sandbox — both the prorated-now and
/// delayed-at-renewal-no-proration paths.</summary>
[Collection(MaxioCollection.Name)]
public class PlanChangeTests
{
    private readonly MaxioFixture _fixture;

    public PlanChangeTests(MaxioFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<int> CreateFreshSubscriptionAsync(string suffix, string productHandle = "eshop-pro")
    {
        var reference = $"xunit-planchange-{suffix}-{Guid.NewGuid():N}@example.com";
        var customer = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");
        var subscription = await _fixture.BillingClient.CreateSubscriptionAsync(customer.Id, productHandle);
        return subscription.Id;
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_ApplyNow_ReturnsProrationFields()
    {
        var subscriptionId = await CreateFreshSubscriptionAsync("preview-now");

        var preview = await _fixture.BillingClient.PreviewPlanChangeAsync(subscriptionId, "basic-plan", applyNow: true);

        Assert.True(preview.ApplyNow);
        Assert.Equal(2900, preview.TargetPriceInCents);
        Assert.NotNull(preview.ChargeInCents);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_AtRenewal_ReturnsTargetPriceAndExplanatoryNote()
    {
        var subscriptionId = await CreateFreshSubscriptionAsync("preview-renewal");

        var preview = await _fixture.BillingClient.PreviewPlanChangeAsync(subscriptionId, "basic-plan", applyNow: false);

        Assert.False(preview.ApplyNow);
        Assert.Equal(2900, preview.TargetPriceInCents);
        Assert.Null(preview.ProratedAdjustmentInCents);
        Assert.False(string.IsNullOrWhiteSpace(preview.Note));
    }

    [Fact]
    public async Task CommitPlanChangeNowAsync_MovesSubscriptionToTargetProductImmediately()
    {
        var subscriptionId = await CreateFreshSubscriptionAsync("commit-now");

        var updated = await _fixture.BillingClient.CommitPlanChangeNowAsync(subscriptionId, "basic-plan");

        Assert.Equal("basic-plan", updated.ProductHandle);
        Assert.Equal(2900, updated.PriceInCents);
    }

    [Fact]
    public async Task SchedulePlanChangeAtRenewalAsync_SchedulesWithoutChangingCurrentProductYet()
    {
        var subscriptionId = await CreateFreshSubscriptionAsync("commit-renewal");

        var updated = await _fixture.BillingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, "basic-plan");

        // Current product is unchanged now; the change is scheduled for the next renewal.
        Assert.Equal("eshop-pro", updated.ProductHandle);
        Assert.Equal("basic-plan", updated.NextProductHandle);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_UnknownTargetHandle_ThrowsBillingConfigurationException()
    {
        var subscriptionId = await CreateFreshSubscriptionAsync("preview-bad-handle");

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.BillingClient.PreviewPlanChangeAsync(subscriptionId, "no-such-plan-handle-xyz", applyNow: true));
    }
}
