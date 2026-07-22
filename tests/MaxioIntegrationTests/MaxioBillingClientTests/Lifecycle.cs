using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Lifecycle
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task PausesASubscriptionOntoTheHoldEndpoint()
    {
        _handler.RespondOk(HttpMethod.Post, "/subscriptions/42/hold.json",
            MaxioJson.SubscriptionResponse(42, "on_hold", 33, UserReference, onHoldAt: "2026-07-20T10:00:00Z"));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.PauseSubscriptionAsync(42, null);

        // The provider's paused state is "on_hold" — distinct from its separate "paused" state.
        Assert.Equal(SubscriptionState.OnHold, updated.State);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero), updated.OnHoldAt);
    }

    [Fact]
    public async Task SchedulesAnAutomaticResumptionWhenOneIsRequested()
    {
        _handler.RespondOk(HttpMethod.Post, "/subscriptions/42/hold.json",
            MaxioJson.SubscriptionResponse(42, "on_hold", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        await client.PauseSubscriptionAsync(42, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("automatically_resume_at", _handler.LastRequest.Body);
    }

    [Fact]
    public async Task ResumesAPausedSubscription()
    {
        _handler.RespondOk(HttpMethod.Post, "/subscriptions/42/resume.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.ResumeSubscriptionAsync(42);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task CancelsImmediatelyByDeletingTheSubscription()
    {
        _handler.RespondOk(HttpMethod.Delete, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "canceled", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.CancelSubscriptionAsync(42, CancellationTiming.Immediate, "not needed");

        Assert.Equal(SubscriptionState.Canceled, updated.State);
        Assert.Contains("not needed", _handler.LastRequest.Body);
    }

    [Fact]
    public async Task CancelsAtEndOfPeriodAndReadsBackThePendingState()
    {
        // The delayed-cancellation endpoint returns only a confirmation message, so the client must
        // re-read the subscription to report the pending state the caller needs to show.
        _handler
            .RespondOk(HttpMethod.Post, "/subscriptions/42/delayed_cancel.json", MaxioJson.DelayedCancellation())
            .RespondOk(HttpMethod.Get, "/subscriptions/42.json",
                MaxioJson.SubscriptionResponse(42, "active", 33, UserReference,
                    cancelAtEndOfPeriod: true, scheduledCancellationAt: "2026-08-01T00:00:00Z"));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.CancelSubscriptionAsync(42, CancellationTiming.EndOfPeriod, null);

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), updated.ScheduledCancellationAt);
        Assert.Single(_handler.RequestsFor("/delayed_cancel.json"));
    }

    [Fact]
    public async Task ReactivatesACancelledSubscription()
    {
        _handler.RespondOk(HttpMethod.Put, "/subscriptions/42/reactivate.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.ReactivateSubscriptionAsync(42);

        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task SurfacesAProviderRefusalOfAPauseWithItsOwnMessage()
    {
        _handler.Respond(HttpMethod.Post, "/subscriptions/42/hold.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Cannot hold a subscription billing within 24 hours."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PauseSubscriptionAsync(42, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("within 24 hours", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAProviderRefusalOfAReactivationWithItsOwnMessage()
    {
        _handler.Respond(HttpMethod.Put, "/subscriptions/42/reactivate.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Subscription cannot be reactivated from this state."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ReactivateSubscriptionAsync(42));

        Assert.Contains("cannot be reactivated", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAProviderRefusalOfAnImmediateCancellation()
    {
        _handler.Respond(HttpMethod.Delete, "/subscriptions/42.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Subscription is already canceled."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(42, CancellationTiming.Immediate, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("already canceled", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesANotFoundOnAnImmediateCancellationOfAnUnknownSubscription()
    {
        _handler.Respond(HttpMethod.Delete, "/subscriptions/999.json", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(999, CancellationTiming.Immediate, null));

        Assert.True(exception.IsNotFound);
    }

    [Fact]
    public async Task SurfacesAProviderRefusalOfADelayedCancellation()
    {
        _handler.Respond(HttpMethod.Post, "/subscriptions/42/delayed_cancel.json",
            HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Cannot schedule a cancellation while past due."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(42, CancellationTiming.EndOfPeriod, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("past due", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderOnResume()
    {
        _handler.Unreachable(HttpMethod.Post, "/subscriptions/42/resume.json");
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ResumeSubscriptionAsync(42));

        Assert.True(exception.IsTransport);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderOnPause()
    {
        _handler.Unreachable(HttpMethod.Post, "/subscriptions/42/hold.json");
        var client = BillingClientBuilder.Build(_handler);

        Assert.True((await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PauseSubscriptionAsync(42, null))).IsTransport);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderOnReactivate()
    {
        _handler.Unreachable(HttpMethod.Put, "/subscriptions/42/reactivate.json");
        var client = BillingClientBuilder.Build(_handler);

        Assert.True((await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ReactivateSubscriptionAsync(42))).IsTransport);
    }

    [Fact]
    public async Task SurfacesANotFoundOnAnUnknownSubscriptionForALifecycleAction()
    {
        _handler.Respond(HttpMethod.Post, "/subscriptions/999/resume.json", HttpStatusCode.NotFound,
            MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ResumeSubscriptionAsync(999));

        Assert.True(exception.IsNotFound);
    }
}
