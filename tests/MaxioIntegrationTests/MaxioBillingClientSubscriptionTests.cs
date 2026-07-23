using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Reading and creating subscriptions, and normalizing the provider's states onto eShopOnWeb's.
/// </summary>
public class MaxioBillingClientSubscriptionTests
{
    private const string Reference = "demouser@microsoft.com";

    [Fact]
    public async Task CreateSubscriptionAsync_MapsTheSubscription()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription()));

        var subscription = await BillingClientFixture.Create(handler)
            .CreateSubscriptionAsync(51234, BillingClientFixture.DefaultPlanHandle);

        Assert.Equal(900001, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(Reference, subscription.CustomerReference);
        Assert.Equal(51234, subscription.CustomerId);
        Assert.NotNull(subscription.CurrentPeriodEndsAt);
        Assert.NotNull(subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsTheCustomerIdAndPlanHandle()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription()));

        await BillingClientFixture.Create(handler).CreateSubscriptionAsync(51234, "eshop-pro");

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);

        var body = request.Body!.Replace(" ", string.Empty);
        Assert.Contains("\"customer_id\":51234", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesAProviderRejection_WithItsMessage()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Product must require a payment method.", "Customer is invalid."),
            (HttpStatusCode)422);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).CreateSubscriptionAsync(51234, "eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Product must require a payment method.", ex.Message);
        Assert.Contains("Customer is invalid.", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReturnsNull_ForAnUnknownId()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.Errors("Not Found"), HttpStatusCode.NotFound);

        Assert.Null(await BillingClientFixture.Create(handler).GetSubscriptionAsync(999999));
    }

    [Fact]
    public async Task GetSubscriptionAsync_MapsAScheduledEndOfPeriodCancellation()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.SubscriptionEnvelope(
            MaxioJson.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: "2024-07-01T00:00:00-04:00")));

        var subscription = await BillingClientFixture.Create(handler).GetSubscriptionAsync(900001);

        Assert.NotNull(subscription);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.NotNull(subscription.DelayedCancelAt);

        // Still active until the period boundary.
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task GetSubscriptionAsync_MapsAScheduledPlanChange()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.SubscriptionEnvelope(
            MaxioJson.Subscription(nextProductHandle: "basic-plan")));

        var subscription = await BillingClientFixture.Create(handler).GetSubscriptionAsync(900001);

        Assert.Equal("basic-plan", subscription!.NextPlanHandle);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_AndSkipsTheSubscriptionCall_ForAnUnknownUser()
    {
        var handler = StubHttpMessageHandler.Sequence(StubResponse.NotFound());

        var subscriptions = await BillingClientFixture.Create(handler)
            .ListSubscriptionsAsync("nobody@example.com");

        Assert.Empty(subscriptions);

        // Only the customer lookup happened; there is no customer to list subscriptions for.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmpty_WhenTheCustomerHasNoSubscriptions()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.CustomerEnvelope(MaxioJson.Customer())),
            StubResponse.Ok("[]"));

        Assert.Empty(await BillingClientFixture.Create(handler).ListSubscriptionsAsync(Reference));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEveryStateAndOrdersNewestFirst()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.CustomerEnvelope(MaxioJson.Customer())),
            StubResponse.Ok(MaxioJson.SubscriptionList(
                MaxioJson.Subscription(id: 900001, state: "canceled"),
                MaxioJson.Subscription(id: 900002, state: "active"))));

        var subscriptions = await BillingClientFixture.Create(handler).ListSubscriptionsAsync(Reference);

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.State == SubscriptionState.Canceled);
        Assert.Contains(subscriptions, s => s.State == SubscriptionState.Active);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("suspended", SubscriptionState.Suspended)]
    [InlineData("unpaid", SubscriptionState.Unpaid)]
    [InlineData("pending", SubscriptionState.Pending)]
    [InlineData("failed_to_create", SubscriptionState.FailedToCreate)]
    // Maxio reports a paused subscription under either name depending on how it was suspended.
    [InlineData("on_hold", SubscriptionState.Paused)]
    [InlineData("paused", SubscriptionState.Paused)]
    // An unmodelled state must degrade, never throw.
    [InlineData("some_future_state", SubscriptionState.Unknown)]
    public async Task GetSubscriptionAsync_NormalizesProviderStates(string providerState, SubscriptionState expected)
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: providerState)));

        var subscription = await BillingClientFixture.Create(handler).GetSubscriptionAsync(900001);

        Assert.Equal(expected, subscription!.State);
        Assert.Equal(providerState, subscription.ProviderState);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("paused")]
    [InlineData("past_due")]
    [InlineData("expired")]
    public async Task IsActive_IsFalse_ForNonBillingStates(string providerState)
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(state: providerState)));

        var subscription = await BillingClientFixture.Create(handler).GetSubscriptionAsync(900001);

        Assert.False(subscription!.IsActive);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ConvertsBalanceCentsToDollars()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription(balanceInCents: 12_345)));

        var subscription = await BillingClientFixture.Create(handler).GetSubscriptionAsync(900001);

        Assert.Equal(123.45m, subscription!.Balance);
    }
}
