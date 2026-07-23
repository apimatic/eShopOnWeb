using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Customer mapping and subscription reads/writes (UC1) and lifecycle transitions (UC4).</summary>
public class MaxioBillingClientSubscriptionTests
{
    [Fact]
    public async Task EnsureCustomerAsync_ReturnsTheExistingCustomer_WithoutCreatingASecondOne()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.CustomerJson);
        var client = TestBillingClient.Create(handler);

        var customer = await client.EnsureCustomerAsync(MaxioPayloads.CustomerReference);

        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);
        Assert.Equal(MaxioPayloads.CustomerReference, customer.Reference);

        var lookup = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, lookup.Method);
        Assert.Equal("/customers/lookup.json", lookup.Path);
        Assert.Contains("reference=demouser%40microsoft.com", lookup.PathAndQuery);
    }

    [Fact]
    public async Task EnsureCustomerAsync_CreatesTheCustomerKeyedOnTheUserReference_WhenTheLookupFindsNothing()
    {
        var handler = StubHttpMessageHandler.InSequence(
            (HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson),
            (HttpStatusCode.Created, MaxioPayloads.CustomerJson));
        var client = TestBillingClient.Create(handler);

        var customer = await client.EnsureCustomerAsync(MaxioPayloads.CustomerReference);

        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);
        Assert.Equal(2, handler.Requests.Count);

        var create = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Equal("/customers.json", create.Path);

        // The reference is what makes the mapping idempotent, and Maxio requires an email and names.
        var body = JsonDocument.Parse(create.Body!).RootElement.GetProperty("customer");
        Assert.Equal(MaxioPayloads.CustomerReference, body.GetProperty("reference").GetString());
        Assert.Equal(MaxioPayloads.CustomerReference, body.GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("first_name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("last_name").GetString()));
    }

    [Fact]
    public async Task EnsureCustomerAsync_SynthesisesAnEmail_WhenTheUserReferenceIsNotAnEmailAddress()
    {
        var handler = StubHttpMessageHandler.InSequence(
            (HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson),
            (HttpStatusCode.Created, MaxioPayloads.CustomerJson));
        var client = TestBillingClient.Create(handler);

        await client.EnsureCustomerAsync("plainusername");

        var body = JsonDocument.Parse(handler.LastRequest.Body!).RootElement.GetProperty("customer");
        Assert.Equal("plainusername@eshoponweb.local", body.GetProperty("email").GetString());
        Assert.Equal("plainusername", body.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task EnsureCustomerAsync_SurfacesAProviderRejectionOnCreate()
    {
        var handler = StubHttpMessageHandler.InSequence(
            (HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson),
            (HttpStatusCode.UnprocessableEntity, """{"errors":["Email: is invalid."]}"""));
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync("someone@example.com"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Email: is invalid.", exception.ProviderErrors);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_EnrollsByCustomerIdAndPlanHandle_AndBillsByRemittance()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.ActiveSubscriptionJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro");

        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        Assert.Equal(299.00m, subscription.Plan.Price);
        Assert.Equal(299.00m, subscription.Balance);
        Assert.Equal(MaxioPayloads.CustomerId, subscription.CustomerId);
        Assert.Equal(MaxioPayloads.CustomerReference, subscription.CustomerReference);
        Assert.True(subscription.IsLive);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement.GetProperty("subscription");
        Assert.Equal(MaxioPayloads.CustomerId, body.GetProperty("customer_id").GetInt32());
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        // The seeded plans have no payment method, so signup must invoice rather than auto-collect.
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesTheProvidersOwnMessage_WhenEnrollmentIsRejected()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.UnprocessableEntity, MaxioPayloads.UnprocessableEntityJson);
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(MaxioPayloads.CustomerId, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.ProviderErrors);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ProjectsEverySubscriptionForTheCustomer()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.SubscriptionListJson);
        var client = TestBillingClient.Create(handler);

        var subscriptions = await client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal($"/customers/{MaxioPayloads.CustomerId}/subscriptions.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ReturnsEmpty_ForACustomerWithNoSubscriptions()
    {
        var handler = StubHttpMessageHandler.ReturningOk("[]");
        var client = TestBillingClient.Create(handler);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId));
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReturnsNull_ForAnUnknownSubscriptionId()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson);
        var client = TestBillingClient.Create(handler);

        Assert.Null(await client.GetSubscriptionAsync(999999));
    }

    [Fact]
    public async Task GetSubscriptionAsync_SurfacesAPendingPlanChange()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.SubscriptionWithPendingPlanChangeJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal("basic-plan", subscription!.Plan.Handle);
        Assert.Equal("eshop-pro", subscription.PendingPlanHandle);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active, true)]
    [InlineData("trialing", SubscriptionState.Active, true)]
    [InlineData("pending", SubscriptionState.Pending, true)]
    [InlineData("past_due", SubscriptionState.PastDue, true)]
    [InlineData("unpaid", SubscriptionState.PastDue, true)]
    [InlineData("on_hold", SubscriptionState.Paused, false)]
    [InlineData("paused", SubscriptionState.Paused, false)]
    [InlineData("canceled", SubscriptionState.Cancelled, false)]
    [InlineData("expired", SubscriptionState.Expired, false)]
    [InlineData("some_future_state", SubscriptionState.Unknown, false)]
    public async Task ProviderStatesMapOntoTheDomainLifecycleStates(string providerState, SubscriptionState expected, bool expectedIsLive)
    {
        var json = MaxioPayloads.ActiveSubscriptionJson.Replace("\"state\": \"active\"", $"\"state\": \"{providerState}\"");
        var client = TestBillingClient.Create(StubHttpMessageHandler.ReturningOk(json));

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(expected, subscription!.State);
        // The verbatim provider state is always preserved, even when it is one we do not model.
        Assert.Equal(providerState, subscription.ProviderState);
        Assert.Equal(expectedIsLive, subscription.IsLive);
    }

    [Fact]
    public async Task PauseSubscriptionAsync_PutsTheSubscriptionOnHold()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.PausedSubscriptionJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.PauseSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.Equal("on_hold", subscription.ProviderState);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/hold.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_RestartsBilling()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.ActiveSubscriptionJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.ResumeSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/resume.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_Immediate_CancelsAtOnceAndSendsTheReason()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.CancelledSubscriptionJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(MaxioPayloads.SubscriptionId, CancellationTiming.Immediate, "too expensive");

        Assert.Equal(SubscriptionState.Cancelled, subscription.State);
        Assert.False(subscription.CancelAtEndOfPeriod);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}.json", request.Path);
        var body = JsonDocument.Parse(request.Body!).RootElement.GetProperty("subscription");
        Assert.Equal("too expensive", body.GetProperty("cancellation_message").GetString());
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_SchedulesTheCancellationAndReadsTheResultBack()
    {
        // Maxio answers a delayed cancel with only a confirmation message, so the state must be
        // re-read from the subscription itself rather than assumed.
        var handler = StubHttpMessageHandler.InSequence(
            (HttpStatusCode.OK, MaxioPayloads.DelayedCancellationJson),
            (HttpStatusCode.OK, MaxioPayloads.PendingCancellationSubscriptionJson));
        var client = TestBillingClient.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, null);

        // Still active, but scheduled to end at the period boundary.
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.NotNull(subscription.DelayedCancelAt);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/delayed_cancel.json", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}.json", handler.Requests[1].Path);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_Throws_WhenTheSubscriptionCannotBeReadBack()
    {
        var handler = StubHttpMessageHandler.InSequence(
            (HttpStatusCode.OK, MaxioPayloads.DelayedCancellationJson),
            (HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson));
        var client = TestBillingClient.Create(handler);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(MaxioPayloads.SubscriptionId, CancellationTiming.EndOfPeriod, null));
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_RestartsACancelledSubscription()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.ActiveSubscriptionJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.ReactivateSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(HttpMethod.Put, handler.LastRequest.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/reactivate.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task ALifecycleTransition_SurfacesTheProvidersRejection_WhenTheStateHasDriftedOutOfBand()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Subscription is not currently on hold."]}""");
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ResumeSubscriptionAsync(MaxioPayloads.SubscriptionId));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Subscription is not currently on hold.", exception.ProviderErrors);
    }

    [Fact]
    public async Task ASubscriptionWithoutItsPlan_IsRejected_RatherThanProjectedWithAFabricatedPlan()
    {
        var handler = StubHttpMessageHandler.ReturningOk("""
            { "subscription": { "id": 1, "state": "active", "balance_in_cents": 0 } }
            """);
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(1));

        Assert.Contains("without its product", exception.Message);
    }
}
