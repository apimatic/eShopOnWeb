using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>UC4 — the four lifecycle transitions against the provider.</summary>
public class MaxioBillingClientLifecycleTests
{
    private static string SubscriptionPath => $"/subscriptions/{MaxioPayloads.SubscriptionId}.json";
    private static string HoldPath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/hold.json";
    private static string ResumePath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/resume.json";
    private static string DelayedCancelPath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/delayed_cancel.json";
    private static string ReactivatePath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/reactivate.json";

    [Fact]
    public async Task PausesThroughTheHoldEndpoint()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, HoldPath, MaxioPayloads.Subscription("on_hold")));

        var subscription = await harness.Client.PauseAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.OnHold, subscription.State);
        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, HoldPath));
    }

    [Fact]
    public async Task ResumesThroughTheResumeEndpointWithoutARequestBody()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, ResumePath, MaxioPayloads.Subscription()));

        var subscription = await harness.Client.ResumeAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);

        // No calendar-billing resumption charge is requested, so none is sent.
        var request = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, ResumePath));
        Assert.DoesNotContain("resumption_charge", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelsImmediatelyThroughTheSubscriptionDeleteEndpoint()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Delete, SubscriptionPath, MaxioPayloads.Subscription("canceled")));

        var subscription = await harness.Client.CancelAsync(
            MaxioPayloads.SubscriptionId, CancellationTiming.Immediate, "too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.False(subscription.CancelAtEndOfPeriod);

        var body = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Delete, SubscriptionPath)).Body;
        Assert.NotNull(body);
        Assert.Contains("\"cancellation_message\":\"too expensive\"", body);

        // An immediate cancel must not touch the deferred endpoint.
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, DelayedCancelPath));
    }

    [Fact]
    public async Task CancelsAtTheEndOfThePeriodThroughTheDelayedCancelEndpointAndReReadsTheState()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, DelayedCancelPath, MaxioPayloads.DelayedCancellation)
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.Subscription(cancelAtEndOfPeriod: true)));

        var subscription = await harness.Client.CancelAsync(
            MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, null);

        // The delayed-cancel endpoint returns only a message, so the provider's own view is read back.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 4, 0, 0, TimeSpan.Zero), subscription.DelayedCancelAt);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, DelayedCancelPath));
        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Get, SubscriptionPath));
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Delete, SubscriptionPath));
    }

    [Fact]
    public async Task FailsWhenTheDeferredCancellationCannotBeReadBack()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, DelayedCancelPath, MaxioPayloads.DelayedCancellation)
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CancelAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, null));

        Assert.Contains("could not be read back", exception.Message);
    }

    [Fact]
    public async Task ReactivatesThroughTheReactivateEndpoint()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Put, ReactivatePath, MaxioPayloads.Subscription()));

        var subscription = await harness.Client.ReactivateAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Single(harness.Handler.RequestsFor(HttpMethod.Put, ReactivatePath));
    }

    [Fact]
    public async Task SurfacesAPauseTheProviderRefuses()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, HoldPath,
                """{"errors":["Cannot hold a subscription within 24 hours of renewal"]}""",
                HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.PauseAsync(MaxioPayloads.SubscriptionId));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("within 24 hours of renewal", exception.Message);
    }

    [Fact]
    public async Task SurfacesADeferredCancellationTheProviderRefuses()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, DelayedCancelPath,
                """{"errors":["Subscription is already pending cancellation"]}""",
                HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CancelAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("already pending cancellation", exception.Message);
    }

    [Fact]
    public async Task SurfacesACancellationRefusedWithASingleErrorMessage()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Delete, SubscriptionPath, """{"error":"Subscription is already canceled"}""", HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CancelAsync(MaxioPayloads.SubscriptionId, CancellationTiming.Immediate, null));

        Assert.Contains("already canceled", exception.Message);
    }

    [Fact]
    public async Task SurfacesALifecycleCallAgainstAnUnknownSubscription()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Put, ReactivatePath, MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.ReactivateAsync(MaxioPayloads.SubscriptionId));

        Assert.Equal(404, exception.StatusCode);
    }
}
