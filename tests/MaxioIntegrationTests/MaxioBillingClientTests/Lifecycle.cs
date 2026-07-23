using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Lifecycle
{
    private readonly RecordingHttpMessageHandler _handler = new();

    private static string Path(string suffix) => $"/subscriptions/{MaxioResponses.SubscriptionId}{suffix}";

    [Fact]
    public async Task PausingPutsTheSubscriptionOnHold()
    {
        _handler.RespondJson(HttpMethod.Post, Path("/hold.json"), MaxioResponses.Subscription("on_hold"));

        var updated = await TestBillingClientFactory.Create(_handler).PauseAsync(MaxioResponses.SubscriptionId);

        Assert.Equal(SubscriptionState.Paused, updated.State);
        Assert.False(updated.IsActive);
        Assert.Equal(HttpMethod.Post, Assert.Single(_handler.Requests).Method);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        _handler.RespondJson(HttpMethod.Post, Path("/resume.json"), MaxioResponses.Subscription("active"));

        var updated = await TestBillingClientFactory.Create(_handler).ResumeAsync(MaxioResponses.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.Equal(Path("/resume.json"), Assert.Single(_handler.Requests).Path);
    }

    [Fact]
    public async Task ReactivatingRevivesACancelledSubscription()
    {
        _handler.RespondJson(HttpMethod.Put, Path("/reactivate.json"), MaxioResponses.Subscription("active"));

        var updated = await TestBillingClientFactory.Create(_handler).ReactivateAsync(MaxioResponses.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, updated.State);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(Path("/reactivate.json"), request.Path);
    }

    [Fact]
    public async Task CancellingImmediatelyStopsTheSubscriptionNow()
    {
        _handler.RespondJson(HttpMethod.Delete, Path(".json"), MaxioResponses.Subscription("canceled"));

        var updated = await TestBillingClientFactory.Create(_handler)
            .CancelAsync(MaxioResponses.SubscriptionId, CancellationTiming.Immediate, "not needed");

        Assert.Equal(SubscriptionState.Canceled, updated.State);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(Path(".json"), request.Path);
        Assert.Contains("\"cancellation_message\":\"not needed\"", request.Body!);
    }

    /// <summary>
    /// An end-of-period cancel must schedule the cancellation at the boundary and leave the
    /// subscription running until then — not stop it now.
    /// </summary>
    /// <remarks>
    /// This route is the one exception in the lifecycle API: it answers with a bare confirmation
    /// message instead of the subscription, so the updated record has to be read back. Treating its
    /// reply as a subscription envelope yields an empty record and a spurious provider failure.
    /// </remarks>
    [Fact]
    public async Task CancellingAtEndOfPeriodSchedulesTheCancellationAtTheBoundary()
    {
        _handler.RespondJson(HttpMethod.Post, Path("/delayed_cancel.json"), MaxioResponses.DelayedCancelAcknowledgement)
                .RespondJson(HttpMethod.Get, Path(".json"),
                    MaxioResponses.Subscription("active", cancelAtEndOfPeriod: true, delayedCancelAt: "2026-08-23T20:12:51+05:00"));

        var updated = await TestBillingClientFactory.Create(_handler)
            .CancelAsync(MaxioResponses.SubscriptionId, CancellationTiming.EndOfPeriod, "switching");

        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.True(updated.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 12, 51, TimeSpan.FromHours(5)), updated.DelayedCancelAt);

        Assert.Collection(_handler.Requests,
            scheduled =>
            {
                Assert.Equal(HttpMethod.Post, scheduled.Method);
                Assert.Equal(Path("/delayed_cancel.json"), scheduled.Path);
            },
            readBack =>
            {
                Assert.Equal(HttpMethod.Get, readBack.Method);
                Assert.Equal(Path(".json"), readBack.Path);
            });

        // The immediate-cancel route must not have been touched.
        Assert.Empty(_handler.RequestsFor(HttpMethod.Delete, Path(".json")));
    }

    [Fact]
    public async Task FailsWhenTheSubscriptionCannotBeReadBackAfterSchedulingAnEndOfPeriodCancel()
    {
        _handler.RespondJson(HttpMethod.Post, Path("/delayed_cancel.json"), MaxioResponses.DelayedCancelAcknowledgement)
                .RespondStatus(HttpMethod.Get, Path(".json"), System.Net.HttpStatusCode.NotFound);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.CancelAsync(MaxioResponses.SubscriptionId, CancellationTiming.EndOfPeriod, null));

        Assert.Contains("could not be read back", exception.Message);
    }

    [Fact]
    public async Task OmitsABlankCancellationReasonRatherThanSendingNull()
    {
        _handler.RespondJson(HttpMethod.Delete, Path(".json"), MaxioResponses.Subscription("canceled"));

        await TestBillingClientFactory.Create(_handler)
            .CancelAsync(MaxioResponses.SubscriptionId, CancellationTiming.Immediate, "   ");

        Assert.DoesNotContain("cancellation_message", Assert.Single(_handler.Requests).Body!);
    }
}
