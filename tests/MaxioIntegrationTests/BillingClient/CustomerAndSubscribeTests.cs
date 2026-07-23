using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

public class CustomerAndSubscribeTests
{
    [Fact]
    public async Task FindCustomerLooksUpByTheEShopOnWebUserReference()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.Customer);
        var client = BillingClientBuilder.Build(handler);

        var customer = await client.FindCustomerByReferenceAsync("shopper@example.com");

        Assert.NotNull(customer);
        Assert.Equal(97883340, customer.Id);
        Assert.Equal("shopper@example.com", customer.Reference);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/customers/lookup.json", request.Path);
        Assert.Contains("reference=shopper%40example.com", request.PathAndQuery);
    }

    [Fact]
    public async Task FindCustomerReturnsNullWhenTheUserHasNoBillingRecordYet()
    {
        // Maxio answers an unknown reference with 404 and an empty body.
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task EnsureCustomerIsIdempotentAndDoesNotCreateWhenOneAlreadyExists()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.Customer);
        var client = BillingClientBuilder.Build(handler);

        var customer = await client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", null, null);

        Assert.Equal(97883340, customer.Id);

        // Exactly one lookup, and no POST — a repeat subscribe must not create a duplicate customer.
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task EnsureCustomerCreatesTheCustomerKeyedOnTheUserReferenceWhenAbsent()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.NotFound, string.Empty)
            .RespondWithJson(MaxioResponses.Customer);

        var client = BillingClientBuilder.Build(handler);

        await client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", null, null);

        Assert.Equal(2, handler.RequestCount);

        var create = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Equal("/customers.json", create.Path);
        Assert.Contains("\"reference\":\"shopper@example.com\"", create.Body);
        Assert.Contains("\"email\":\"shopper@example.com\"", create.Body);

        // Maxio requires a name; it is derived, never fabricated from personal data.
        Assert.Contains("\"first_name\":\"shopper\"", create.Body);
        Assert.Contains("\"last_name\":\"eShopOnWeb\"", create.Body);
    }

    [Fact]
    public async Task EnsureCustomerPrefersSuppliedNamesOverDerivedOnes()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.NotFound, string.Empty)
            .RespondWithJson(MaxioResponses.Customer);

        var client = BillingClientBuilder.Build(handler);

        await client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", "Ada", "Lovelace");

        Assert.Contains("\"first_name\":\"Ada\"", handler.Requests[1].Body);
        Assert.Contains("\"last_name\":\"Lovelace\"", handler.Requests[1].Body);
    }

    [Fact]
    public async Task CreateSubscriptionRequestsInvoiceBillingSoNoPaymentMethodIsNeeded()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.Created,
            MaxioResponses.ActiveSubscription);

        var client = BillingClientBuilder.Build(handler);

        await client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions.json", request.Path);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
        Assert.Contains("\"customer_reference\":\"shopper@example.com\"", request.Body);

        // Without this, Maxio rejects a priced plan with "No payment method was on file".
        Assert.Contains("\"payment_collection_method\":\"remittance\"", request.Body);
    }

    [Fact]
    public async Task CreateSubscriptionMapsTheProviderStateAndBillingDates()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.Created,
            MaxioResponses.ActiveSubscription);

        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro");

        Assert.Equal(93491347, subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("active", subscription.ProviderState);
        Assert.True(subscription.IsLive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(299.00m, subscription.Balance);
        Assert.Equal(97883340, subscription.CustomerId);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 21, 57, TimeSpan.FromHours(5)),
            subscription.CurrentPeriodEndsAt);
        Assert.False(subscription.CancelAtEndOfPeriod);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerMapsEveryEntry()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.SubscriptionList);
        var client = BillingClientBuilder.Build(handler);

        var subscriptions = await client.ListSubscriptionsForCustomerAsync(97883340);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(93491347, subscription.Id);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal("/customers/97883340/subscriptions.json", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerReturnsEmptyForACustomerWithNone()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.EmptyList);
        var client = BillingClientBuilder.Build(handler);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(97883340));
    }

    [Fact]
    public async Task GetSubscriptionReturnsNullForAnUnknownId()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.GetSubscriptionAsync(999999999));
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("on_hold", SubscriptionStatus.OnHold)]
    [InlineData("paused", SubscriptionStatus.Paused)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("expired", SubscriptionStatus.Expired)]
    [InlineData("trial_ended", SubscriptionStatus.TrialEnded)]
    [InlineData("unpaid", SubscriptionStatus.Unpaid)]
    [InlineData("suspended", SubscriptionStatus.Suspended)]
    [InlineData("failed_to_create", SubscriptionStatus.Failed)]
    [InlineData("something_new", SubscriptionStatus.Unknown)]
    public async Task MapsEveryProviderStateOntoTheDomainVocabulary(string providerState,
        SubscriptionStatus expected)
    {
        var payload = """
            {"subscription":{"id":1,"state":"PROVIDER_STATE","product":{"handle":"eshop-pro"},
            "customer":{"id":2,"reference":"shopper@example.com"}}}
            """.Replace("PROVIDER_STATE", providerState);

        var handler = new StubHttpMessageHandler().RespondWithJson(payload);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.GetSubscriptionAsync(1);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription.Status);

        // The raw provider state is always preserved for support and diagnostics.
        Assert.Equal(providerState, subscription.ProviderState);
    }

    [Fact]
    public async Task OnHoldIsDistinctFromMaxiosInternalPausedState()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.OnHoldSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.GetSubscriptionAsync(93491347);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.OnHold, subscription.Status);
        Assert.NotEqual(SubscriptionStatus.Paused, subscription.Status);
        Assert.False(subscription.IsLive);
    }
}
