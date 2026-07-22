using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Pause, resume, cancel and reactivate (UC4), including the two very different cancellations.
/// </summary>
public class LifecycleOperations
{
    [Fact]
    public async Task PausingPutsTheSubscriptionOnHold()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/hold.json", HttpStatusCode.OK,
            MaxioJson.Subscription(state: "on_hold"));

        var subscription = await harness.Client.PauseSubscriptionAsync(MaxioJson.SubscriptionId);

        // Maxio's hold endpoint yields on_hold, which this integration normalizes to Paused.
        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.Equal("on_hold", subscription.ProviderState);
        Assert.True(subscription.CanResume);
        Assert.False(subscription.CanPause);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/resume.json", HttpStatusCode.OK,
            MaxioJson.Subscription(state: "active"));

        var subscription = await harness.Client.ResumeSubscriptionAsync(MaxioJson.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task CancellingImmediatelyDeletesTheSubscriptionAndPassesTheReason()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Delete, $"/subscriptions/{MaxioJson.SubscriptionId}.json",
            HttpStatusCode.OK, MaxioJson.Subscription(state: "canceled"));

        var subscription = await harness.Client.CancelSubscriptionAsync(
            MaxioJson.SubscriptionId, CancellationTiming.Immediate, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.True(subscription.CanReactivate);
        Assert.False(subscription.CanCancel);

        var request = harness.Handler.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("Too expensive", request.Body);
    }

    [Fact]
    public async Task CancellingAtEndOfPeriodSchedulesTheCancellationAndRereadsTheSubscription()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/delayed_cancel.json", HttpStatusCode.OK,
            MaxioJson.DelayedCancellation());
        harness.Handler.Respond(HttpMethod.Get, $"/subscriptions/{MaxioJson.SubscriptionId}.json",
            HttpStatusCode.OK,
            MaxioJson.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: "2026-08-01T00:00:00-04:00"));

        var subscription = await harness.Client.CancelSubscriptionAsync(
            MaxioJson.SubscriptionId, CancellationTiming.EndOfPeriod, "Switching providers");

        // The delayed-cancel endpoint answers with a message only, so the effective date the
        // customer is shown has to come from a fresh read.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.DelayedCancelAt);

        // The subscription keeps billing until the boundary, so it is still active.
        Assert.Equal(SubscriptionState.Active, subscription.State);

        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Contains("/delayed_cancel.json", harness.Handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, harness.Handler.Requests[1].Method);
    }

    [Fact]
    public async Task ImmediateAndEndOfPeriodCancellationsUseDifferentEndpoints()
    {
        using var immediate = MaxioTestHarness.Create();
        immediate.Handler.Respond(HttpMethod.Delete, "/subscriptions/", HttpStatusCode.OK,
            MaxioJson.Subscription(state: "canceled"));
        await immediate.Client.CancelSubscriptionAsync(MaxioJson.SubscriptionId, CancellationTiming.Immediate, null);

        using var endOfPeriod = MaxioTestHarness.Create();
        endOfPeriod.Handler.Respond(HttpMethod.Post, "/delayed_cancel.json", HttpStatusCode.OK,
            MaxioJson.DelayedCancellation());
        endOfPeriod.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.OK,
            MaxioJson.Subscription(cancelAtEndOfPeriod: true));
        await endOfPeriod.Client.CancelSubscriptionAsync(MaxioJson.SubscriptionId, CancellationTiming.EndOfPeriod, null);

        // Confusing the two would either cut a customer off early or keep billing them.
        Assert.DoesNotContain("delayed_cancel", immediate.Handler.Requests[0].Uri.AbsolutePath);
        Assert.Contains("delayed_cancel", endOfPeriod.Handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task ReactivatingBringsACancelledSubscriptionBack()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Put, "/reactivate.json", HttpStatusCode.OK,
            MaxioJson.Subscription(state: "active"));

        var subscription = await harness.Client.ReactivateSubscriptionAsync(MaxioJson.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenAPauseIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/hold.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Only active subscriptions can be held"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.PauseSubscriptionAsync(MaxioJson.SubscriptionId));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Only active subscriptions can be held", exception.ProviderMessages);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenACancellationIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Delete, "/subscriptions/", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Subscription is already canceled"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CancelSubscriptionAsync(MaxioJson.SubscriptionId, CancellationTiming.Immediate, null));

        Assert.Contains("Subscription is already canceled", exception.ProviderMessages);
    }

    [Fact]
    public async Task SurfacesAProviderErrorWhenAReactivationTargetDoesNotExist()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Put, "/reactivate.json", HttpStatusCode.NotFound, "{}");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.ReactivateSubscriptionAsync(4242));

        Assert.Equal(404, exception.StatusCode);
    }
}
