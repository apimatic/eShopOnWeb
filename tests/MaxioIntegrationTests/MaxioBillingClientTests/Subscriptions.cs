using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Subscriptions
{
    [Fact]
    public async Task CreateSubscriptionEnrollsTheCustomerInTheRequestedPlan()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription, HttpStatusCode.Created);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.CreateSubscriptionAsync(555001, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(BillingSubscriptionState.Active, subscription.State);

        var body = handler.LastRequest.Body.Replace(" ", string.Empty);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":555001", body);
    }

    [Fact]
    public async Task CreateSubscriptionConvertsMoneyFieldsFromCents()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription, HttpStatusCode.Created);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.CreateSubscriptionAsync(555001, "eshop-pro");

        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(12.34m, subscription.Balance);
    }

    [Fact]
    public async Task GetSubscriptionMapsStateDatesAndCustomerLink()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.Equal(BillingSubscriptionState.Active, subscription!.State);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(555001, subscription.CustomerId);
        Assert.Equal("shopper@example.com", subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.NextBillingAt);
        Assert.False(subscription.CancelAtEndOfPeriod);
    }

    [Fact]
    public async Task GetSubscriptionReturnsNullForAnUnknownId()
    {
        var handler = StubHttpMessageHandler.Always(string.Empty, HttpStatusCode.NotFound);
        var client = BillingClientFixture.Create(handler);

        Assert.Null(await client.GetSubscriptionAsync(404404));
    }

    [Fact]
    public async Task MapsTheProviderHoldStateOntoThePausedDomainState()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.PausedSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.GetSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Paused, subscription!.State);
        Assert.Equal("on_hold", subscription.ProviderState);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsReturnsEachSubscription()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.SubscriptionList);
        var client = BillingClientFixture.Create(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync(555001);

        var only = Assert.Single(subscriptions);
        Assert.Equal(15236915, only.Id);
        Assert.Equal(299.00m, only.PlanPrice);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsReturnsEmptyWhenTheCustomerHasNone()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.EmptyList);
        var client = BillingClientFixture.Create(handler);

        Assert.Empty(await client.ListCustomerSubscriptionsAsync(555001));
    }

    [Fact]
    public async Task CreateSubscriptionSurfacesAProviderRejectionAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ValidationErrors,
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(555001, "no-such-plan"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product handle is invalid", exception.Message);
    }
}
