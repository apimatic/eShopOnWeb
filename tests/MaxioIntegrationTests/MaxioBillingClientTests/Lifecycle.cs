using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Lifecycle
{
    [Fact]
    public async Task PausePutsTheSubscriptionOnHold()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.PausedSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.PauseSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Paused, subscription.State);
        Assert.Contains("hold", handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ResumeReturnsTheSubscriptionToActive()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.ResumeSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task CancelSendsTheReasonAndReturnsTheCancelledSubscription()
    {
        const string canceled = """
            {
              "subscription": {
                "id": 15236915,
                "state": "canceled",
                "balance_in_cents": 0,
                "cancellation_message": "too expensive",
                "customer": { "id": 555001, "reference": "shopper@example.com" },
                "product": { "id": 7126957, "handle": "eshop-pro", "price_in_cents": 29900 }
              }
            }
            """;

        var handler = StubHttpMessageHandler.Always(canceled);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(15236915, "too expensive");

        Assert.Equal(BillingSubscriptionState.Canceled, subscription.State);
        Assert.Contains("\"cancellation_message\":\"tooexpensive\"",
            handler.LastRequest.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task CancelAtEndOfPeriodReadsTheSubscriptionBackBecauseTheProviderOnlyReturnsAMessage()
    {
        var handler = StubHttpMessageHandler.Sequence(
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.DelayedCancellationAccepted),
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.SubscriptionPendingEndOfPeriodCancellation));

        var client = BillingClientFixture.Create(handler);

        var subscription = await client.CancelSubscriptionAtEndOfPeriodAsync(15236915, "switching");

        // The delayed-cancellation call answers with a bare message, so the state has to be re-read.
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)),
            subscription.ScheduledCancellationAt);
    }

    [Fact]
    public async Task CancelAtEndOfPeriodFailsWhenTheSubscriptionCannotBeReadBack()
    {
        var handler = StubHttpMessageHandler.Sequence(
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.DelayedCancellationAccepted),
            new StubResponse(HttpStatusCode.NotFound, string.Empty));

        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAtEndOfPeriodAsync(15236915, null));

        Assert.Equal(502, exception.StatusCode);
    }

    [Fact]
    public async Task ReactivateReturnsTheSubscriptionToActive()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.ReactivateSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.Contains("reactivate", handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ARejectedTransitionSurfacesAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ValidationErrors,
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PauseSubscriptionAsync(15236915));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task AnUnknownSubscriptionSurfacesAsANotFoundTypedException()
    {
        var handler = StubHttpMessageHandler.Always(string.Empty, HttpStatusCode.NotFound);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(404404, null));

        Assert.Equal(404, exception.StatusCode);
    }
}
