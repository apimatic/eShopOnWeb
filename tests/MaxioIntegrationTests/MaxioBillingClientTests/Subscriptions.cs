using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Subscriptions
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReadsASubscriptionWithItsStatePeriodAndPlan()
    {
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        Assert.NotNull(subscription);
        Assert.Equal(42, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(UserReference, subscription.CustomerReference);
        Assert.Equal(33, subscription.CustomerId);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), subscription.CurrentPeriodStartedAt);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task ReadsTheSubscriptionPriceInWholeCurrencyUnits()
    {
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        // 29900 cents on the wire must be $299.00 to the customer.
        Assert.Equal(299.00m, subscription!.PlanPrice);
        Assert.Equal("USD", subscription.Currency);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownSubscriptionId()
    {
        _handler.Respond(HttpMethod.Get, "/subscriptions/999.json", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        Assert.Null(await client.GetSubscriptionAsync(999));
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("on_hold", SubscriptionState.OnHold)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("unpaid", SubscriptionState.Unpaid)]
    [InlineData("expired", SubscriptionState.Expired)]
    public async Task MapsEachProviderStateOntoTheDomainState(string wireState, SubscriptionState expected)
    {
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, wireState, 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        Assert.Equal(expected, subscription!.State);
    }

    [Fact]
    public async Task MapsAnUnrecognisedProviderStateToUnknownRatherThanGuessing()
    {
        // The provider's states are open strings; a state we do not model must never be mistaken
        // for an actionable one.
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "some_future_state", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        Assert.Equal(SubscriptionState.Unknown, subscription!.State);
        Assert.False(subscription.IsLive);
        Assert.Empty(subscription.AllowedActions);
    }

    [Fact]
    public async Task ListsEverySubscriptionHeldByACustomer()
    {
        _handler.RespondOk(HttpMethod.Get, "/customers/33/subscriptions.json",
            MaxioJson.SubscriptionList(
                MaxioJson.Subscription(42, "active", 33, UserReference),
                MaxioJson.Subscription(43, "canceled", 33, UserReference, "basic-plan", MaxioJson.BasicPlanId, "Basic Plan", MaxioJson.BasicPlanPriceInCents)));
        var client = BillingClientBuilder.Build(_handler);

        var subscriptions = await client.ListSubscriptionsForCustomerAsync(33);

        Assert.Equal(2, subscriptions.Count);
        Assert.Single(subscriptions, subscription => subscription.IsLive);
        Assert.Equal(29.00m, subscriptions.Single(s => s.Id == 43).PlanPrice);
    }

    [Fact]
    public async Task ReturnsEmptyWhenACustomerHasNoSubscriptions()
    {
        _handler.RespondOk(HttpMethod.Get, "/customers/33/subscriptions.json", "[]");
        var client = BillingClientBuilder.Build(_handler);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(33));
    }

    [Fact]
    public async Task CreatesASubscriptionForACustomerOnAPlanHandle()
    {
        _handler.RespondOk(HttpMethod.Post, "/subscriptions.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.CreateSubscriptionAsync(33, "eshop-pro");

        Assert.Equal(42, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        var body = _handler.LastRequest.Body;
        Assert.Contains("\"customer_id\":33", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
    }

    [Fact]
    public async Task EnrolsWithRemittanceCollectionSoNoPaymentMethodIsDemanded()
    {
        // This integration never captures a card. Under automatic collection the provider tries to
        // charge the first period immediately and refuses the enrolment outright when there is no
        // payment profile, so new subscriptions must be created for invoicing instead.
        _handler.RespondOk(HttpMethod.Post, "/subscriptions.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        await client.CreateSubscriptionAsync(33, "eshop-pro");

        Assert.Contains("\"payment_collection_method\":\"remittance\"", _handler.LastRequest.Body);
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("invoice")]
    [InlineData("prepaid")]
    [InlineData("remittance")]
    public async Task HonoursAConfiguredCollectionMethod(string configured)
    {
        _handler.RespondOk(HttpMethod.Post, "/subscriptions.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference));
        var settings = BillingClientBuilder.Settings();
        settings.PaymentCollectionMethod = configured;
        var client = BillingClientBuilder.Build(_handler, settings);

        await client.CreateSubscriptionAsync(33, "eshop-pro");

        Assert.Contains($"\"payment_collection_method\":\"{configured}\"", _handler.LastRequest.Body);
    }

    [Fact]
    public async Task RejectsAnUnrecognisedCollectionMethodRatherThanBillingWrongly()
    {
        var settings = BillingClientBuilder.Settings();
        settings.PaymentCollectionMethod = "cheque-in-the-post";
        var client = BillingClientBuilder.Build(_handler, settings);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.CreateSubscriptionAsync(33, "eshop-pro"));

        Assert.Contains("PaymentCollectionMethod", exception.Message);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfAnEnrolmentWithItsOwnMessage()
    {
        _handler.Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Product: is required.", "Payment profile: is required."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(33, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product: is required.", exception.ProviderMessage);
        Assert.Contains("Payment profile: is required.", exception.ProviderMessage);
    }

    [Fact]
    public async Task ReportsAPendingEndOfPeriodCancellation()
    {
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference,
                cancelAtEndOfPeriod: true, scheduledCancellationAt: "2026-08-01T00:00:00Z"));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        Assert.True(subscription!.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.ScheduledCancellationAt);

        // A second delayed cancellation must not be offered while one is already pending.
        Assert.False(subscription.CanApply(SubscriptionLifecycleAction.Cancel, CancellationTiming.EndOfPeriod));
    }

    [Fact]
    public async Task ReportsAPendingPlanChange()
    {
        _handler.RespondOk(HttpMethod.Get, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference,
                nextProductHandle: "basic-plan", nextProductId: MaxioJson.BasicPlanId));
        var client = BillingClientBuilder.Build(_handler);

        var subscription = await client.GetSubscriptionAsync(42);

        Assert.True(subscription!.HasPendingPlanChange);
        Assert.Equal("basic-plan", subscription.PendingPlanHandle);
    }
}
