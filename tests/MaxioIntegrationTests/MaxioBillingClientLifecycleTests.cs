using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC4 — pause, resume, cancel (immediate and end-of-period), and reactivate.
/// </summary>
public class MaxioBillingClientLifecycleTests
{
    [Fact]
    public async Task PauseSubscriptionAsync_HoldsTheSubscription()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: "on_hold")));

        var updated = await BillingClientFixture.Create(handler).PauseSubscriptionAsync(900001);

        Assert.Equal(SubscriptionState.Paused, updated.State);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("hold", request.Path);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_ReturnsTheSubscriptionToActive()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: "active")));

        var updated = await BillingClientFixture.Create(handler).ResumeSubscriptionAsync(900001);

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.Contains("resume", handler.LastRequest.Path);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_ReturnsTheSubscriptionToActive()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: "active")));

        var updated = await BillingClientFixture.Create(handler).ReactivateSubscriptionAsync(900001);

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.Contains("reactivate", handler.LastRequest.Path);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_Immediate_CancelsRightAway()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: "canceled")));

        var updated = await BillingClientFixture.Create(handler)
            .CancelSubscriptionAsync(900001, CancellationTiming.Immediate, reason: null);

        Assert.Equal(SubscriptionState.Canceled, updated.State);
        Assert.False(updated.IsActive);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Delete, request.Method);

        // Only one call: an immediate cancel returns the subscription directly.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_Immediate_SendsTheReason_WhenOneIsGiven()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: "canceled")));

        await BillingClientFixture.Create(handler)
            .CancelSubscriptionAsync(900001, CancellationTiming.Immediate, "too expensive");

        Assert.Contains("too expensive", handler.LastRequest.Body!);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_SchedulesThenRereadsTheAuthoritativeState()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.DelayedCancellation()),
            StubResponse.Ok(MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(
                cancelAtEndOfPeriod: true, delayedCancelAt: "2024-07-01T00:00:00-04:00"))));

        var updated = await BillingClientFixture.Create(handler)
            .CancelSubscriptionAsync(900001, CancellationTiming.EndOfPeriod, reason: null);

        // The delayed-cancel endpoint returns only a message, so the state must be re-read.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("delayed_cancel", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);

        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.NotNull(updated.DelayedCancelAt);

        // It keeps billing until the period boundary.
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.UnprocessableEntity(MaxioJson.Errors("Subscription is already pending cancellation.")));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler)
                .CancelSubscriptionAsync(900001, CancellationTiming.EndOfPeriod, reason: null));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("already pending cancellation", ex.Message);
    }

    [Fact]
    public async Task PauseSubscriptionAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Subscription is canceled and cannot be held."), (HttpStatusCode)422);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).PauseSubscriptionAsync(900001));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("cannot be held", ex.Message);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Subscription is not on hold."), (HttpStatusCode)422);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ResumeSubscriptionAsync(900001));

        Assert.Contains("not on hold", ex.Message);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Subscription is already active."), (HttpStatusCode)422);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ReactivateSubscriptionAsync(900001));

        Assert.Contains("already active", ex.Message);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_SurfacesAMissingSubscription()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Not Found"), HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler)
                .CancelSubscriptionAsync(999999, CancellationTiming.Immediate, reason: null));

        Assert.True(ex.IsNotFound);
    }
}
