using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class SubscriptionReads
{
    [Fact]
    public async Task ListsEverySubscriptionHeldByACustomer()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("customers/55/subscriptions.json", MaxioJson.SubscriptionList(
            MaxioJson.Subscription(101, "active"),
            MaxioJson.Subscription(102, "canceled")));

        var subscriptions = await builder.Build().ListSubscriptionsForCustomerAsync(55);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(new[] { 101, 102 }, subscriptions.Select(s => s.Id));
    }

    [Fact]
    public async Task ReturnsAnEmptyListForACustomerWithNoSubscriptions()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("customers/55/subscriptions.json", "[]");

        Assert.Empty(await builder.Build().ListSubscriptionsForCustomerAsync(55));
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenTheCustomerIdIsUnknown()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("customers/999/subscriptions.json");

        Assert.Empty(await builder.Build().ListSubscriptionsForCustomerAsync(999));
    }

    [Fact]
    public async Task MapsTheSubscriptionOntoTheDomainAggregate()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101.json",
            $$"""{ "subscription": {{MaxioJson.Subscription(101, "active")}} }""");

        var subscription = await builder.Build().GetSubscriptionAsync(101);

        Assert.NotNull(subscription);
        Assert.Equal(101, subscription!.Id);
        Assert.Equal(101, subscription.ProviderSubscriptionId);
        Assert.Equal(55, subscription.ProviderCustomerId);
        // The eShopOnWeb user reference travels on the Maxio customer record.
        Assert.Equal("demo@microsoft.com", subscription.BuyerId);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        Assert.Equal(299.00m, subscription.Plan.Price);
        Assert.Equal(2026, subscription.CurrentPeriodEndsAt!.Value.Year);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownSubscriptionId()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("subscriptions/424242.json");

        Assert.Null(await builder.Build().GetSubscriptionAsync(424242));
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active, true)]
    [InlineData("trialing", SubscriptionState.Trialing, true)]
    [InlineData("on_hold", SubscriptionState.Paused, false)]
    [InlineData("paused", SubscriptionState.Paused, false)]
    [InlineData("canceled", SubscriptionState.Canceled, false)]
    [InlineData("expired", SubscriptionState.Expired, false)]
    [InlineData("past_due", SubscriptionState.PastDue, false)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded, false)]
    [InlineData("unpaid", SubscriptionState.Unpaid, false)]
    [InlineData("something_new", SubscriptionState.Unknown, false)]
    public async Task MapsMaxioStatesOntoTheProviderAgnosticVocabulary(string providerState,
        SubscriptionState expected, bool expectedActive)
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101.json",
            $$"""{ "subscription": {{MaxioJson.Subscription(101, providerState)}} }""");

        var subscription = await builder.Build().GetSubscriptionAsync(101);

        Assert.Equal(expected, subscription!.State);
        Assert.Equal(expectedActive, subscription.IsActive);
    }

    [Fact]
    public async Task SubscribesTheCustomerToThePlanByHandle()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("subscriptions.json", System.Net.HttpStatusCode.Created,
            MaxioJson.SubscriptionResponse(101, "active"));

        var subscription = await builder.Build().CreateSubscriptionAsync(55, "eshop-pro");

        Assert.Equal(101, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);

        using var body = JsonDocument.Parse(request.Body!);
        var payload = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", payload.GetProperty("product_handle").GetString());
        Assert.Equal(55, payload.GetProperty("customer_id").GetInt32());
    }

    [Fact]
    public async Task EnrolsOnRemittanceSoNoPaymentMethodIsDemandedAtSignup()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("subscriptions.json", System.Net.HttpStatusCode.Created,
            MaxioJson.SubscriptionResponse(101, "active"));

        await builder.Build().CreateSubscriptionAsync(55, "eshop-pro");

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);

        // Maxio defaults to "automatic" collection, which fails signup when the plan
        // deliberately does not require a card.
        Assert.Equal("remittance",
            body.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
    }
}
