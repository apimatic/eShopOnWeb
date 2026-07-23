using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC4 — the four lifecycle transitions and the two cancellation timings.</summary>
public class LifecycleTests
{
    [Fact]
    public async Task PausingPutsTheSubscriptionOnHold()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.OnHoldSubscription);

        var subscription = await client.PauseSubscriptionAsync(90001, null);

        Assert.Equal(SubscriptionStatus.OnHold, subscription.Status);
        Assert.Equal("on_hold", subscription.ProviderState);
        Assert.False(subscription.IsActive);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task AnIndefinitePauseSendsNoAutoResumeSchedule()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.OnHoldSubscription);

        await client.PauseSubscriptionAsync(90001, null);

        Assert.DoesNotContain("automatically_resume_at", handler.LastRequestBody);
    }

    [Fact]
    public async Task APauseWithAnAutoResumeDateSchedulesTheResumption()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.OnHoldSubscription);

        await client.PauseSubscriptionAsync(90001, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("automatically_resume_at", handler.LastRequestBody);
        Assert.Contains("2026-09-01", handler.LastRequestBody);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.ActiveSubscription);

        var subscription = await client.ResumeSubscriptionAsync(90001);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task AnImmediateCancellationCancelsTheSubscriptionNow()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.CanceledSubscription);

        var subscription = await client.CancelSubscriptionAsync(90001, CancellationTiming.Immediate, "too expensive");

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero), subscription.CanceledAt);
        Assert.False(subscription.CancelAtEndOfPeriod);

        Assert.Single(handler.Requests);
        Assert.Contains("too expensive", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnEndOfPeriodCancellationDefersToThePeriodBoundaryAndReadsTheStateBack()
    {
        // The delayed-cancel endpoint returns only a message, so the resulting state must be re-read.
        var (client, handler) = BillingClientFixture.Create(
            ProviderPayloads.DelayedCancellationAccepted,
            ProviderPayloads.PendingCancellationSubscription);

        var subscription = await client.CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, "switching");

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.DelayedCancelAt);

        // It is still active until the boundary is reached.
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task TheTwoCancellationTimingsUseDifferentProviderEndpoints()
    {
        var (immediateClient, immediateHandler) = BillingClientFixture.Create(ProviderPayloads.CanceledSubscription);
        var (deferredClient, deferredHandler) = BillingClientFixture.Create(
            ProviderPayloads.DelayedCancellationAccepted,
            ProviderPayloads.PendingCancellationSubscription);

        await immediateClient.CancelSubscriptionAsync(90001, CancellationTiming.Immediate, null);
        await deferredClient.CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, null);

        Assert.NotEqual(immediateHandler.Requests[0].RequestUri!.AbsolutePath,
            deferredHandler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AnEndOfPeriodCancellationThatCannotBeReadBackIsReportedRatherThanGuessed()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.DelayedCancellationAccepted);
        handler.RespondWith(ProviderPayloads.NotFoundError, HttpStatusCode.NotFound);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, null));

        Assert.Equal("CancelSubscriptionAtPeriodEnd", exception.Operation);
    }

    [Fact]
    public async Task ReactivatingBringsACancelledSubscriptionBack()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.ActiveSubscription);

        var subscription = await client.ReactivateSubscriptionAsync(90001);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task AProviderRejectionOfALifecycleTransitionSurfacesAsATypedException()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ValidationError, HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ResumeSubscriptionAsync(90001));

        Assert.Equal("ResumeSubscription", exception.Operation);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    [Fact]
    public async Task AWriteThatReturnsNoSubscriptionIsReportedRatherThanSilentlySucceeding()
    {
        var (client, _) = BillingClientFixture.Create("""{"subscription": null}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ReactivateSubscriptionAsync(90001));

        Assert.Contains("returned no subscription", exception.ProviderMessage);
    }
}
