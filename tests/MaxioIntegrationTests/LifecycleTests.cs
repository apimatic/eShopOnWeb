using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC4 — pause, resume, cancel (immediately or at period end) and reactivate.
/// </summary>
public class LifecycleTests
{
    [Fact]
    public async Task Pausing_puts_the_subscription_on_hold()
    {
        var server = new StubBillingServer()
            .Post("hold.json", BillingJson.SubscriptionEnvelope(BillingJson.Subscription(1001, state: "on_hold")));

        var updated = await BillingTestHarness.Build(server).PauseSubscriptionAsync(1001);

        Assert.Equal(SubscriptionLifecycleState.Paused, updated.State);
        Assert.False(updated.IsBillable);
    }

    [Fact]
    public async Task Surfaces_the_providers_reason_when_a_pause_is_refused()
    {
        var server = new StubBillingServer()
            .Post("hold.json",
                BillingJson.Errors("Cannot hold a subscription billing within 24 hours."),
                HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).PauseSubscriptionAsync(1001));

        Assert.Contains("24 hours", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resuming_returns_the_subscription_to_active()
    {
        var server = new StubBillingServer()
            .Post("resume.json", BillingJson.SubscriptionEnvelope(BillingJson.Subscription(1001, state: "active")));

        var updated = await BillingTestHarness.Build(server).ResumeSubscriptionAsync(1001);

        Assert.Equal(SubscriptionLifecycleState.Active, updated.State);
    }

    [Fact]
    public async Task Cancelling_immediately_ends_the_subscription_now()
    {
        var server = new StubBillingServer()
            .Delete("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: "canceled")));

        var updated = await BillingTestHarness.Build(server)
            .CancelSubscriptionAsync(1001, CancellationTiming.Immediate, "not needed any more");

        Assert.Equal(SubscriptionLifecycleState.Canceled, updated.State);
        Assert.False(updated.CancelAtEndOfPeriod);

        Assert.Contains("not needed any more",
            Assert.Single(server.RequestsFor("/subscriptions/1001.json")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_at_period_end_schedules_it_and_reports_the_refreshed_subscription()
    {
        var server = new StubBillingServer()
            .Post("delayed_cancel.json", BillingJson.DelayedCancellation("Delayed cancellation created"))
            // The delayed-cancel endpoint answers with a message only, so the caller's view has to be
            // refreshed from the provider before it can be shown.
            .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001,
                    state: "active",
                    cancelAtEndOfPeriod: true,
                    delayedCancelAt: "2026-08-22T00:00:00-04:00")));

        var updated = await BillingTestHarness.Build(server)
            .CancelSubscriptionAsync(1001, CancellationTiming.EndOfPeriod, "too expensive");

        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.NotNull(updated.DelayedCancelAt);
        // It is still active until the period boundary.
        Assert.Equal(SubscriptionLifecycleState.Active, updated.State);

        Assert.Single(server.RequestsFor("delayed_cancel.json"));
    }

    [Fact]
    public async Task Reactivating_a_pending_cancellation_revokes_the_schedule_without_reactivating()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: "active", cancelAtEndOfPeriod: true,
                    delayedCancelAt: "2026-08-22T00:00:00-04:00")))
            .Delete("delayed_cancel.json", BillingJson.DelayedCancellation("Delayed cancellation removed"));

        // After the revoke, the refreshed read shows a clean active subscription.
        server.Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
            BillingJson.Subscription(1001, state: "active")));

        var updated = await BillingTestHarness.Build(server).ReactivateSubscriptionAsync(1001);

        Assert.Equal(SubscriptionLifecycleState.Active, updated.State);
        Assert.False(updated.CancelAtEndOfPeriod);

        Assert.Single(server.RequestsFor("delayed_cancel.json"));
        // A subscription that never actually ended must not be pushed through reactivation.
        Assert.Empty(server.RequestsFor("reactivate.json"));
    }

    [Fact]
    public async Task Reactivating_a_cancelled_subscription_brings_it_back()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: "canceled")))
            .Put("reactivate.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: "active")));

        var updated = await BillingTestHarness.Build(server).ReactivateSubscriptionAsync(1001);

        Assert.Equal(SubscriptionLifecycleState.Active, updated.State);
        Assert.Single(server.RequestsFor("reactivate.json"));
    }

    [Fact]
    public async Task Reactivating_an_unknown_subscription_reports_it_as_not_found()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/4242.json", BillingJson.NotFound(), HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => BillingTestHarness.Build(server).ReactivateSubscriptionAsync(4242));
    }

    [Fact]
    public async Task Surfaces_a_refused_reactivation_as_a_typed_billing_exception()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: "canceled")))
            .Put("reactivate.json",
                BillingJson.Errors("Subscription cannot be reactivated."), HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).ReactivateSubscriptionAsync(1001));

        Assert.Contains("cannot be reactivated", exception.ProviderMessage, StringComparison.Ordinal);
    }
}
