using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Subscriptions
{
    private const string SubscriptionsPath = "/subscriptions.json";

    private readonly RecordingHttpMessageHandler _handler = new();

    private static string SubscriptionPath => $"/subscriptions/{MaxioResponses.SubscriptionId}.json";

    private static string CustomerSubscriptionsPath => $"/customers/{MaxioResponses.CustomerId}/subscriptions.json";

    [Fact]
    public async Task CreatesASubscriptionAndMapsTheProviderRecord()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);

        var subscription = await TestBillingClientFactory.Create(_handler)
            .CreateSubscriptionAsync(new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro"));

        Assert.Equal(MaxioResponses.SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal("Pro Plan", subscription.ProductName);
        Assert.Equal(MaxioResponses.CustomerId, subscription.CustomerId);
        Assert.Equal("USD", subscription.Currency);
    }

    [Fact]
    public async Task ReportsTheSubscriptionPriceInBothCentsAndDollars()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);

        var subscription = await TestBillingClientFactory.Create(_handler)
            .CreateSubscriptionAsync(new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro"));

        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal(299.00m, subscription.ProductPrice);
        Assert.Equal(29900, subscription.BalanceInCents);
        Assert.Equal(299.00m, subscription.Balance);
    }

    [Fact]
    public async Task CarriesThePeriodBoundariesAndTheNextBillingDate()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);

        var subscription = await TestBillingClientFactory.Create(_handler)
            .CreateSubscriptionAsync(new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro"));

        Assert.Equal(new DateTimeOffset(2026, 7, 23, 20, 12, 8, TimeSpan.FromHours(5)), subscription.CurrentPeriodStartsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 12, 8, TimeSpan.FromHours(5)), subscription.CurrentPeriodEndsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 12, 8, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
    }

    /// <summary>
    /// A site with no payment gateway rejects automatic collection outright, so the configured
    /// collection method has to reach the provider.
    /// </summary>
    [Fact]
    public async Task SendsTheConfiguredPaymentCollectionMethod()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);

        await TestBillingClientFactory.Create(_handler)
            .CreateSubscriptionAsync(new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro"));

        var body = Assert.Single(_handler.Requests).Body!;
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains($"\"customer_id\":{MaxioResponses.CustomerId}", body);
    }

    [Fact]
    public async Task LetsAnExplicitCollectionMethodOverrideTheConfiguredDefault()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);

        await TestBillingClientFactory.Create(_handler).CreateSubscriptionAsync(
            new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro", "automatic"));

        Assert.Contains("\"payment_collection_method\":\"automatic\"", Assert.Single(_handler.Requests).Body!);
    }

    /// <summary>A blank configured method must be omitted so the provider applies its own default.</summary>
    [Fact]
    public async Task OmitsTheCollectionMethodEntirelyWhenNoneIsConfigured()
    {
        _handler.RespondJson(HttpMethod.Post, SubscriptionsPath, MaxioResponses.Subscription(), HttpStatusCode.Created);
        var settings = TestBillingClientFactory.Settings(s => s.PaymentCollectionMethod = string.Empty);

        await TestBillingClientFactory.Create(_handler, settings)
            .CreateSubscriptionAsync(new CreateSubscriptionRequest(MaxioResponses.CustomerId, "eshop-pro"));

        Assert.DoesNotContain("payment_collection_method", Assert.Single(_handler.Requests).Body!);
    }

    [Fact]
    public async Task ReadsASubscriptionById()
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionPath, MaxioResponses.Subscription());

        var subscription = await TestBillingClientFactory.Create(_handler)
            .GetSubscriptionAsync(MaxioResponses.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(MaxioResponses.SubscriptionId, subscription.Id);
    }

    [Fact]
    public async Task ReturnsNullForASubscriptionIdTheProviderDoesNotKnow()
    {
        _handler.RespondStatus(HttpMethod.Get, "/subscriptions/999999999.json", HttpStatusCode.NotFound);

        var subscription = await TestBillingClientFactory.Create(_handler).GetSubscriptionAsync(999999999);

        Assert.Null(subscription);
    }

    [Fact]
    public async Task ListsTheSubscriptionsBelongingToACustomer()
    {
        _handler.RespondJson(HttpMethod.Get, CustomerSubscriptionsPath, MaxioResponses.SubscriptionList());

        var subscriptions = await TestBillingClientFactory.Create(_handler)
            .ListSubscriptionsForCustomerAsync(MaxioResponses.CustomerId);

        Assert.Equal(MaxioResponses.SubscriptionId, Assert.Single(subscriptions).Id);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionForACustomerWithNoSubscriptions()
    {
        _handler.RespondJson(HttpMethod.Get, CustomerSubscriptionsPath, MaxioResponses.EmptyArray);

        var subscriptions = await TestBillingClientFactory.Create(_handler)
            .ListSubscriptionsForCustomerAsync(MaxioResponses.CustomerId);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionWhenTheCustomerItselfIsUnknown()
    {
        _handler.RespondStatus(HttpMethod.Get, "/customers/424242/subscriptions.json", HttpStatusCode.NotFound);

        var subscriptions = await TestBillingClientFactory.Create(_handler).ListSubscriptionsForCustomerAsync(424242);

        Assert.Empty(subscriptions);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active, true)]
    [InlineData("trialing", SubscriptionState.Trialing, true)]
    // Maxio calls a paused subscription "on_hold".
    [InlineData("on_hold", SubscriptionState.Paused, false)]
    [InlineData("canceled", SubscriptionState.Canceled, false)]
    [InlineData("expired", SubscriptionState.Expired, false)]
    [InlineData("past_due", SubscriptionState.PastDue, false)]
    [InlineData("soft_failure", SubscriptionState.SoftFailure, false)]
    [InlineData("unpaid", SubscriptionState.Unpaid, false)]
    [InlineData("failed_to_create", SubscriptionState.Failed, false)]
    [InlineData("suspended", SubscriptionState.Suspended, false)]
    [InlineData("pending", SubscriptionState.Pending, false)]
    // A state this integration does not model must never be mistaken for an active one.
    [InlineData("some_future_state", SubscriptionState.Unknown, false)]
    public async Task MapsProviderStatesOntoTheDomainLifecycle(string providerState, SubscriptionState expected, bool expectedActive)
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionPath, MaxioResponses.Subscription(providerState));

        var subscription = await TestBillingClientFactory.Create(_handler)
            .GetSubscriptionAsync(MaxioResponses.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription.State);
        Assert.Equal(expectedActive, subscription.IsActive);
    }

    [Fact]
    public async Task SurfacesAScheduledEndOfPeriodCancellation()
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionPath,
            MaxioResponses.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: "2026-08-23T20:12:51+05:00"));

        var subscription = await TestBillingClientFactory.Create(_handler)
            .GetSubscriptionAsync(MaxioResponses.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 12, 51, TimeSpan.FromHours(5)), subscription.DelayedCancelAt);
        // The subscription keeps running until the boundary.
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task SurfacesAPlanChangeScheduledForTheNextRenewal()
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionPath, MaxioResponses.Subscription(nextProductHandle: "basic-plan"));

        var subscription = await TestBillingClientFactory.Create(_handler)
            .GetSubscriptionAsync(MaxioResponses.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal("basic-plan", subscription.NextProductHandle);
        // The current plan has not moved yet.
        Assert.Equal("eshop-pro", subscription.ProductHandle);
    }
}
