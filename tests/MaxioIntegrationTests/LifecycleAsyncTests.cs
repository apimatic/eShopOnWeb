using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class LifecycleAsyncTests
{
    [Fact]
    public async Task PauseSubscriptionReturnsTheHeldState()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "subscription": { "id": 5001, "state": "on_hold" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.PauseSubscriptionAsync(5001);

        Assert.Equal(SubscriptionStatus.Paused, subscription.Status);
    }

    [Fact]
    public async Task PauseSubscriptionThrowsBillingProviderExceptionWhenRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Cannot place a subscription on hold that renews within 24 hours"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.PauseSubscriptionAsync(5001));

        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task ResumeSubscriptionReturnsToActive()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "subscription": { "id": 5001, "state": "active" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.ResumeSubscriptionAsync(5001);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task CancelImmediateReturnsCanceledState()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "subscription": { "id": 5001, "state": "canceled" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(5001, endOfPeriod: false, reason: "no longer needed");

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }

    [Fact]
    public async Task CancelImmediateThrowsSubscriptionNotFoundForAnUnknownId()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.NotFound));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => client.CancelSubscriptionAsync(999999, endOfPeriod: false, reason: null));

        Assert.Equal(999999, ex.SubscriptionId);
    }

    [Fact]
    public async Task CancelAtEndOfPeriodInitiatesDelayedCancellationThenRereadsTheSubscription()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "message": "Cancellation scheduled" }"""),
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "subscription": { "id": 5001, "state": "active", "cancel_at_end_of_period": true } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(5001, endOfPeriod: true, reason: "customer request");

        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Contains("/delayed_cancel", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReactivateSubscriptionReturnsToActive()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "subscription": { "id": 5001, "state": "active" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.ReactivateSubscriptionAsync(5001);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task ReactivateSubscriptionThrowsBillingProviderExceptionWhenRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Subscription cannot be reactivated"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ReactivateSubscriptionAsync(5001));

        Assert.Equal(422, ex.StatusCode);
    }
}
