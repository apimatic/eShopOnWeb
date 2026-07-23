using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class Lifecycle
{
    private const string SUBSCRIPTION_PATH = "/subscriptions/15236915.json";
    private const string HOLD_PATH = "/subscriptions/15236915/hold.json";
    private const string RESUME_PATH = "/subscriptions/15236915/resume.json";
    private const string REACTIVATE_PATH = "/subscriptions/15236915/reactivate.json";
    private const string DELAYED_CANCEL_PATH = "/subscriptions/15236915/delayed_cancel.json";

    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task PausingPutsTheSubscriptionOnHold()
    {
        _builder.Stub.Respond(HttpMethod.Post, HOLD_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "on_hold", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var paused = await _builder.Build().PauseSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionState.OnHold, paused.State);
        Assert.False(paused.IsActive);
        Assert.Equal(HOLD_PATH, _builder.Stub.LastRequest.PathAndQuery);
        Assert.Equal(HttpMethod.Post, _builder.Stub.LastRequest.Method);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        _builder.Stub.Respond(HttpMethod.Post, RESUME_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var resumed = await _builder.Build().ResumeSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionState.Active, resumed.State);
        Assert.True(resumed.IsActive);
        Assert.Equal(RESUME_PATH, _builder.Stub.LastRequest.PathAndQuery);
    }

    [Fact]
    public async Task ReactivatingUsesThePutVerbTheProviderRequires()
    {
        _builder.Stub.Respond(HttpMethod.Put, REACTIVATE_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var reactivated = await _builder.Build().ReactivateSubscriptionAsync(15236915);

        Assert.Equal(SubscriptionState.Active, reactivated.State);
        Assert.Equal(HttpMethod.Put, _builder.Stub.LastRequest.Method);
        Assert.Equal(REACTIVATE_PATH, _builder.Stub.LastRequest.PathAndQuery);
    }

    [Fact]
    public async Task CancellingImmediatelyDeletesTheSubscriptionAndCarriesTheReason()
    {
        _builder.Stub.Respond(HttpMethod.Delete, SUBSCRIPTION_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "canceled", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var cancelled = await _builder.Build()
            .CancelSubscriptionAsync(15236915, CancellationTiming.Immediately, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, cancelled.State);

        var request = _builder.Stub.LastRequest;
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(SUBSCRIPTION_PATH, request.PathAndQuery);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Too expensive",
            body.RootElement.GetProperty("subscription").GetProperty("cancellation_message").GetString());
    }

    [Fact]
    public async Task CancellingAtPeriodEndSchedulesTheCancellationAndReportsTheProvidersOwnView()
    {
        // The delayed-cancel route answers with a message, not a subscription, so the client must
        // re-read the subscription to report what actually happened.
        _builder.Stub.Respond(HttpMethod.Post, DELAYED_CANCEL_PATH,
            "{\"message\":\"This subscription will be canceled at the end of the period\"}");
        _builder.Stub.Respond(HttpMethod.Get, SUBSCRIPTION_PATH,
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS,
                cancelAtEndOfPeriod: true, delayedCancelAt: "2026-08-23T12:00:00-05:00")));

        var cancelled = await _builder.Build()
            .CancelSubscriptionAsync(15236915, CancellationTiming.EndOfPeriod, "Switching providers");

        // Still active — it cancels at the boundary, it has not cancelled yet.
        Assert.Equal(SubscriptionState.Active, cancelled.State);
        Assert.True(cancelled.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(-5)), cancelled.DelayedCancelAt);

        Assert.Equal(new[] { DELAYED_CANCEL_PATH, SUBSCRIPTION_PATH },
            _builder.Stub.Requests.Select(request => request.PathAndQuery));
    }

    [Fact]
    public async Task SurfacesAnAlreadyCancelledSubscriptionAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, DELAYED_CANCEL_PATH, HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("The subscription is already canceled"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CancelSubscriptionAsync(15236915, CancellationTiming.EndOfPeriod, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("The subscription is already canceled", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task SurfacesARefusedPauseAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, HOLD_PATH, HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Cannot hold a canceled subscription"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().PauseSubscriptionAsync(15236915));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Cannot hold a canceled subscription", exception.Message);
    }

    [Fact]
    public async Task SurfacesAnUnknownSubscriptionOnATransitionAsATypedException()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Put, REACTIVATE_PATH, HttpStatusCode.NotFound, "{}");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ReactivateSubscriptionAsync(15236915));

        Assert.Equal(404, exception.StatusCode);
    }
}
