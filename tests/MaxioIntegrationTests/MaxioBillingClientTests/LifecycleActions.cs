using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class LifecycleActions
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task PauseHoldsTheSubscriptionAndReportsItAsPaused()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(state: "on_hold")));

        var subscription = await BillingClientFixture.Create(_handler).PauseSubscriptionAsync(90210);

        // Maxio calls the held state "on_hold"; the domain models it as Paused.
        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.False(subscription.IsActive);
        Assert.True(subscription.CanResume);
        Assert.Contains("/subscriptions/90210/hold.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task PausedIsAlsoAcceptedAsTheHeldState()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(state: "paused")));

        var subscription = await BillingClientFixture.Create(_handler).PauseSubscriptionAsync(90210);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
    }

    [Fact]
    public async Task ResumeBringsTheSubscriptionBackToActive()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        var subscription = await BillingClientFixture.Create(_handler).ResumeSubscriptionAsync(90210);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Contains("/subscriptions/90210/resume.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ImmediateCancelReportsTheSubscriptionAsCancelled()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(state: "canceled")));

        var subscription = await BillingClientFixture.Create(_handler)
            .CancelSubscriptionAsync(90210, CancellationTiming.Immediate, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.False(subscription.CanCancel);
        Assert.True(subscription.CanReactivate);

        var request = _handler.LastRequest;
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("Too expensive", request.Body);
    }

    [Fact]
    public async Task EndOfPeriodCancelSchedulesTheCancellationAndReadsTheResultingStateBack()
    {
        _handler.RespondWithJson(ProviderPayloads.DelayedCancellationAccepted);
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(cancelAtEndOfPeriod: true)));

        var subscription = await BillingClientFixture.Create(_handler)
            .CancelSubscriptionAsync(90210, CancellationTiming.EndOfPeriod, "Switching plans");

        // The delayed-cancel call only returns a message, so the state must come from a read-back.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.NotNull(subscription.CurrentPeriodEndsAt);

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains("delayed_cancel.json", _handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, _handler.Requests[1].Method);
    }

    [Fact]
    public async Task ReactivateBringsACancelledSubscriptionBackToLife()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        var subscription = await BillingClientFixture.Create(_handler).ReactivateSubscriptionAsync(90210);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Contains("/subscriptions/90210/reactivate.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ReportsAMissingSubscriptionRatherThanInventingASuccess()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler)
                .CancelSubscriptionAsync(404404, CancellationTiming.Immediate, null));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task SurfacesARefusedTransitionWithTheProvidersReason()
    {
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": ["Cannot resume a subscription that is not on hold."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).ResumeSubscriptionAsync(90210));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("not on hold", exception.ProviderMessage);
    }
}
