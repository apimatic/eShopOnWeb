using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class Lifecycle
{
    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task PausePostsToHoldAndReturnsTheOnHoldSubscription()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions/15236915/hold.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription(state: "on_hold"));

        var subscription = await _builder.Build().PauseAsync(15236915, null);

        Assert.Equal(SubscriptionState.OnHold, subscription.State);
        Assert.False(subscription.IsLive);
    }

    [Fact]
    public async Task PauseSendsTheAutomaticResumptionDateWhenOneIsGiven()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions/15236915/hold.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription(state: "on_hold"));

        await _builder.Build().PauseAsync(15236915, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("\"automatically_resume_at\":\"2026-09-01T00:00:00+00:00\"",
            Assert.Single(_builder.Handler.Requests).Body);
    }

    [Fact]
    public async Task ResumePostsWithNoRequestBody()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions/15236915/resume.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription());

        var subscription = await _builder.Build().ResumeAsync(15236915);

        Assert.Equal(SubscriptionState.Active, subscription.State);

        // The spec declares no request body for resume.
        Assert.Null(Assert.Single(_builder.Handler.Requests).Body);
    }

    [Fact]
    public async Task ImmediateCancelDeletesTheSubscriptionAndPassesTheReason()
    {
        _builder.Handler.RespondWith(HttpMethod.Delete, "subscriptions/15236915.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription(state: "canceled"));

        var subscription = await _builder.Build().CancelAsync(15236915, endOfPeriod: false, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);

        var request = Assert.Single(_builder.Handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("\"cancellation_message\":\"Too expensive\"", request.Body);
    }

    [Fact]
    public async Task EndOfPeriodCancelDefersAndRereadsTheSubscription()
    {
        _builder.Handler
            .RespondWith(HttpMethod.Post, "subscriptions/15236915/delayed_cancel.json", HttpStatusCode.OK,
                MaxioPayloads.DelayedCancellation)
            .RespondWith(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.OK,
                MaxioPayloads.Subscription(cancelAtEndOfPeriod: true));

        var subscription = await _builder.Build().CancelAsync(15236915, endOfPeriod: true, "Switching plans");

        // The delayed-cancel endpoint only returns a message, so the state and effective date the
        // actor is shown must come from a re-read.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(2, _builder.Handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, _builder.Handler.Requests.Last().Method);
    }

    [Fact]
    public async Task EndOfPeriodCancelSurfacesAProviderRefusal()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions/15236915/delayed_cancel.json",
            HttpStatusCode.UnprocessableEntity, MaxioPayloads.SingleError);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CancelAsync(15236915, endOfPeriod: true, null));

        // The cancel endpoints can return the single-"error" shape rather than an "errors" array.
        Assert.Contains("The subscription is already canceled", exception.Errors);
    }

    [Fact]
    public async Task ReactivateUsesPutAsTheSpecificationRequires()
    {
        _builder.Handler.RespondWith(HttpMethod.Put, "subscriptions/15236915/reactivate.json",
            HttpStatusCode.OK, MaxioPayloads.Subscription());

        var subscription = await _builder.Build().ReactivateAsync(15236915);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(HttpMethod.Put, Assert.Single(_builder.Handler.Requests).Method);
    }

    [Fact]
    public async Task PauseSurfacesAnIneligibleSubscription()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions/15236915/hold.json",
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["This subscription is not eligible to be put on hold."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().PauseAsync(15236915, null));

        Assert.Contains("This subscription is not eligible to be put on hold.", exception.Errors);
    }
}
