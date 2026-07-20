using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC4 lifecycle transitions against the real sandbox — a single ordered journey per subscription
/// (pause/resume/cancel/reactivate are a state machine, so each subscription's steps run sequentially
/// within one test rather than across independently-ordered test methods).</summary>
[Collection(MaxioCollection.Name)]
public class LifecycleTests
{
    private readonly MaxioFixture _fixture;

    public LifecycleTests(MaxioFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<BillingSubscription> CreateFreshSubscriptionAsync(string suffix)
    {
        var reference = $"xunit-lifecycle-{suffix}-{Guid.NewGuid():N}@example.com";
        var customer = await _fixture.BillingClient.EnsureCustomerAsync(reference, reference, "XUnit", "Tester");
        return await _fixture.BillingClient.CreateSubscriptionAsync(customer.Id, "eshop-pro");
    }

    [Fact]
    public async Task PauseThenResume_RoundTripsSubscriptionState()
    {
        var subscription = await CreateFreshSubscriptionAsync("pause-resume");
        Assert.Equal(SubscriptionLifecycleState.Active, subscription.State);

        var paused = await _fixture.BillingClient.PauseSubscriptionAsync(subscription.Id);
        Assert.Equal(SubscriptionLifecycleState.Paused, paused.State);

        var resumed = await _fixture.BillingClient.ResumeSubscriptionAsync(subscription.Id);
        Assert.Equal(SubscriptionLifecycleState.Active, resumed.State);
    }

    [Fact]
    public async Task CancelImmediately_ThenReactivate_RoundTripsSubscriptionState()
    {
        var subscription = await CreateFreshSubscriptionAsync("cancel-reactivate");

        var cancelled = await _fixture.BillingClient.CancelSubscriptionAsync(subscription.Id, endOfPeriod: false, reason: "xunit test cancel");
        Assert.Equal(SubscriptionLifecycleState.Canceled, cancelled.State);

        var reactivated = await _fixture.BillingClient.ReactivateSubscriptionAsync(subscription.Id);
        Assert.NotEqual(SubscriptionLifecycleState.Canceled, reactivated.State);
    }

    [Fact]
    public async Task CancelAtEndOfPeriod_SetsCancelAtEndOfPeriodFlag()
    {
        var subscription = await CreateFreshSubscriptionAsync("cancel-eop");

        var updated = await _fixture.BillingClient.CancelSubscriptionAsync(subscription.Id, endOfPeriod: true, reason: "xunit test end-of-period cancel");

        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.NotEqual(SubscriptionLifecycleState.Canceled, updated.State);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_OnActiveSubscription_ThrowsBillingProviderException()
    {
        var subscription = await CreateFreshSubscriptionAsync("reactivate-active");
        Assert.Equal(SubscriptionLifecycleState.Active, subscription.State);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => _fixture.BillingClient.ReactivateSubscriptionAsync(subscription.Id));
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_OnActiveNotPausedSubscription_ThrowsBillingProviderException()
    {
        var subscription = await CreateFreshSubscriptionAsync("resume-active");
        Assert.Equal(SubscriptionLifecycleState.Active, subscription.State);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => _fixture.BillingClient.ResumeSubscriptionAsync(subscription.Id));
    }
}
