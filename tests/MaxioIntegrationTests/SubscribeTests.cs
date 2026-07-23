using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC1 — customer creation (idempotent on the user reference) and enrollment.</summary>
public class SubscribeTests
{
    private const string UserReference = "demouser@microsoft.com";

    [Fact]
    public async Task FindCustomerByReferenceMapsTheProviderRecord()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));

        var customer = await context.Client.FindCustomerByReferenceAsync(UserReference);

        Assert.NotNull(customer);
        Assert.Equal(97865317, customer!.Id);
        Assert.Equal(UserReference, customer.Reference);
        Assert.Equal(UserReference, customer.Email);
        Assert.Equal("Demo", customer.FirstName);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullWhenTheUserHasNoProviderRecord()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());

        // A user who has never subscribed is not an error condition.
        Assert.Null(await context.Client.FindCustomerByReferenceAsync(UserReference));
    }

    [Fact]
    public async Task EnsureCustomerIsIdempotentAndDoesNotCreateASecondRecord()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));

        var customer = await context.Client.EnsureCustomerAsync(UserReference, UserReference, "Demo", "User");

        Assert.Equal(97865317, customer.Id);
        // The whole point of the reference: a repeat subscribe must not create a duplicate customer.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, "customers.json"));
    }

    [Fact]
    public async Task EnsureCustomerCreatesTheRecordWhenAbsentAndStampsTheUserReference()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());
        context.Server.MapPost("customers.json", FakeResponse.Created(MaxioPayloads.Customer));

        var customer = await context.Client.EnsureCustomerAsync(UserReference, UserReference, "Demo", "User");

        Assert.Equal(97865317, customer.Id);

        var request = context.Server.LastRequest(HttpMethod.Post, "customers.json");
        Assert.NotNull(request);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", request!.Body);
        Assert.Contains("\"first_name\":\"Demo\"", request.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", request.Body);
    }

    [Fact]
    public async Task CreateSubscriptionEnrollsByPlanHandleAndCustomerReference()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost("subscriptions.json", FakeResponse.Created(MaxioPayloads.ActiveProSubscription));

        var subscription = await context.Client.CreateSubscriptionAsync(UserReference, "eshop-pro");

        Assert.Equal(93482336, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(UserReference, subscription.UserReference);
        Assert.Equal(97865317, subscription.CustomerId);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 11, 44, 53, TimeSpan.FromHours(5)),
            subscription.CurrentPeriodEndsAt);

        var request = context.Server.LastRequest(HttpMethod.Post, "subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request!.Body);
        Assert.Contains("\"customer_reference\":\"demouser@microsoft.com\"", request.Body);
        // Remittance is what lets the demo enroll without capturing a card.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", request.Body);
    }

    [Fact]
    public async Task CreateSubscriptionSurfacesTheProvidersOwnRejectionMessage()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost("subscriptions.json", FakeResponse.Unprocessable(MaxioPayloads.ErrorArray));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.CreateSubscriptionAsync(UserReference, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("No payment method was on file", exception.Message);
        Assert.Equal("No payment method was on file for the $299.00 balance", exception.ProviderMessage);
    }

    [Fact]
    public async Task CreateSubscriptionReadsTheObjectShapedErrorPayloadToo()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost("subscriptions.json", FakeResponse.Unprocessable(MaxioPayloads.ErrorObject));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.CreateSubscriptionAsync(UserReference, "eshop-pro"));

        Assert.Contains("customer: can't be blank", exception.Message);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsTheUsersEnrollments()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));
        context.Server.MapGet("customers/97865317/subscriptions.json",
            FakeResponse.Ok(MaxioPayloads.SubscriptionList));

        var subscriptions = await context.Client.ListSubscriptionsAsync(UserReference);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(93482336, subscription.Id);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWithoutAskingWhenTheUserHasNoCustomerRecord()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference), FakeResponse.NotFound());

        var subscriptions = await context.Client.ListSubscriptionsAsync(UserReference);

        Assert.Empty(subscriptions);
        // No customer means there is nothing to list; asking anyway would be a wasted round trip.
        Assert.DoesNotContain(context.Server.Requests, r => r.PathAndQuery.Contains("subscriptions.json"));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenTheCustomerExistsButHasNoSubscriptions()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.CustomerLookupRoute(UserReference),
            FakeResponse.Ok(MaxioPayloads.Customer));
        context.Server.MapGet("customers/97865317/subscriptions.json", FakeResponse.Ok(MaxioPayloads.EmptyList));

        Assert.Empty(await context.Client.ListSubscriptionsAsync(UserReference));
    }

    [Fact]
    public async Task GetSubscriptionReturnsNullForAnUnknownId()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/999999.json", FakeResponse.NotFound());

        Assert.Null(await context.Client.GetSubscriptionAsync(999999));
    }

    [Fact]
    public async Task GetSubscriptionMapsAKnownId()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/93482336.json", FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var subscription = await context.Client.GetSubscriptionAsync(93482336);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionState.Active, subscription!.State);
        Assert.Equal(299.00m, subscription.PlanPrice);
    }

    [Fact]
    public async Task PendingCancellationIsReportedWithItsEffectiveDate()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/93482336.json",
            FakeResponse.Ok(MaxioPayloads.PendingCancellationSubscription));

        var subscription = await context.Client.GetSubscriptionAsync(93482336);

        Assert.True(subscription!.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 11, 44, 53, TimeSpan.FromHours(5)), subscription.DelayedCancelAt);
        // Still billing until the boundary, so it is still active.
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task AnUnrecognisedProviderStateMapsToUnknownRatherThanThrowing()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet("subscriptions/93482336.json", FakeResponse.Ok("""
            { "subscription": { "id": 93482336, "state": "some_future_state",
              "customer": { "id": 1, "reference": "x" },
              "product": { "handle": "eshop-pro" } } }
            """));

        var subscription = await context.Client.GetSubscriptionAsync(93482336);

        Assert.Equal(SubscriptionState.Unknown, subscription!.State);
        Assert.False(subscription.IsActive);
    }
}
