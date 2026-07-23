using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>Creating and reading subscriptions (UC1 steps 4–5).</summary>
public class MaxioBillingClientSubscriptionTests
{
    [Fact]
    public async Task EnrollsTheCustomerByReferenceOnThePlanHandle()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, "/subscriptions.json", MaxioPayloads.Subscription(), HttpStatusCode.Created));

        var subscription = await harness.Client.CreateSubscriptionAsync(MaxioPayloads.CustomerReference, "eshop-pro");

        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(MaxioPayloads.CustomerReference, subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 4, 0, 0, TimeSpan.Zero), subscription.NextAssessmentAt);

        var body = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, "/subscriptions.json")).Body;
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains($"\"customer_reference\":\"{MaxioPayloads.CustomerReference}\"", body);

        // The demo plans capture no card, so the subscription must not be created on automatic collection.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task SurfacesARejectedEnrollmentWithTheProvidersOwnValidationMessages()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, "/subscriptions.json",
                """{"errors":["Payment profile is required","Product is archived"]}""",
                HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateSubscriptionAsync(MaxioPayloads.CustomerReference, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Payment profile is required", exception.Message);
        Assert.Contains("Product is archived", exception.Message);
    }

    [Fact]
    public async Task FailsWhenTheProviderAcceptsTheEnrollmentButReturnsNoSubscription()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Post, "/subscriptions.json", "{}", HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateSubscriptionAsync(MaxioPayloads.CustomerReference, "eshop-pro"));

        Assert.Contains("returned no subscription", exception.Message);
    }

    [Fact]
    public async Task ReadsASingleSubscriptionById()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, $"/subscriptions/{MaxioPayloads.SubscriptionId}.json", MaxioPayloads.Subscription("on_hold")));

        var subscription = await harness.Client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionState.OnHold, subscription.State);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task ReturnsNothingForAnUnknownSubscriptionId()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/subscriptions/424242.json", MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        Assert.Null(await harness.Client.GetSubscriptionAsync(424_242));
    }

    [Fact]
    public async Task ListsTheSubscriptionsBelongingToACustomerReference()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.Customer)
            .Map(HttpMethod.Get, $"/customers/{MaxioPayloads.CustomerId}/subscriptions.json", MaxioPayloads.SubscriptionList()));

        var subscriptions = await harness.Client.ListSubscriptionsAsync(MaxioPayloads.CustomerReference);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal(MaxioPayloads.CustomerReference, subscription.CustomerReference);
    }

    [Fact]
    public async Task ReturnsAnEmptyListForAUserWithNoProviderCustomerRecord()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        Assert.Empty(await harness.Client.ListSubscriptionsAsync("nobody@microsoft.com"));
    }

    [Fact]
    public async Task ReturnsAnEmptyListForACustomerWithNoSubscriptions()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.Customer)
            .Map(HttpMethod.Get, $"/customers/{MaxioPayloads.CustomerId}/subscriptions.json", MaxioPayloads.EmptyList));

        Assert.Empty(await harness.Client.ListSubscriptionsAsync(MaxioPayloads.CustomerReference));
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("on_hold", SubscriptionState.OnHold)]
    [InlineData("paused", SubscriptionState.Paused)]
    [InlineData("unpaid", SubscriptionState.Unpaid)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("soft_failure", SubscriptionState.SoftFailure)]
    [InlineData("something_new_maxio_invented", SubscriptionState.Unknown)]
    public async Task ProjectsTheProvidersStateOntoTheDomainState(string wireState, SubscriptionState expected)
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, $"/subscriptions/{MaxioPayloads.SubscriptionId}.json", MaxioPayloads.Subscription(wireState)));

        var subscription = await harness.Client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.Equal(expected, subscription!.State);
    }
}
