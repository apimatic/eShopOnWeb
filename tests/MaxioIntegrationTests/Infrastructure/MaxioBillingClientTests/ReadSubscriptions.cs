using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class ReadSubscriptions
{
    private const string Reference = "demouser@microsoft.com";
    private const string LookupPath = "customers/lookup.json?reference=demouser@microsoft.com";

    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task ListsTheCustomersSubscriptionsWithPlanAndState()
    {
        _builder.Handler
            .RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.OK, MaxioPayloads.Customer)
            .RespondWith(HttpMethod.Get, "customers/88001/subscriptions.json", HttpStatusCode.OK,
                MaxioPayloads.SubscriptionList(
                    MaxioPayloads.Subscription(),
                    MaxioPayloads.Subscription(id: 15236916, state: "canceled", planHandle: "basic-plan",
                        planName: "Basic Plan", planPriceInCents: 2900)));

        var subscriptions = await _builder.Build().ListSubscriptionsAsync(Reference);

        Assert.Equal(2, subscriptions.Count);

        var active = subscriptions.First();
        Assert.Equal(15236915, active.Id);
        Assert.Equal(SubscriptionState.Active, active.State);
        Assert.Equal("eshop-pro", active.PlanHandle);
        Assert.Equal(29900, active.PlanPriceInCents);
        Assert.Equal(299.00m, active.PlanPrice);
        Assert.Equal(Reference, active.CustomerReference);
        Assert.Equal(88001, active.CustomerId);
        Assert.True(active.IsLive);

        var cancelled = subscriptions.Last();
        Assert.Equal(SubscriptionState.Canceled, cancelled.State);
        Assert.False(cancelled.IsLive);
    }

    [Fact]
    public async Task ReturnsEmptyForAnUnknownCustomerReference()
    {
        _builder.Handler.RespondWith(HttpMethod.Get,
            "customers/lookup.json?reference=nobody@example.com", HttpStatusCode.NotFound, string.Empty);

        var subscriptions = await _builder.Build().ListSubscriptionsAsync("nobody@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsEmptyWhenAKnownCustomerHasNoSubscriptions()
    {
        _builder.Handler
            .RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.OK, MaxioPayloads.Customer)
            .RespondWith(HttpMethod.Get, "customers/88001/subscriptions.json", HttpStatusCode.OK, "[]");

        var subscriptions = await _builder.Build().ListSubscriptionsAsync(Reference);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownSubscriptionId()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "subscriptions/999999999.json",
            HttpStatusCode.NotFound, string.Empty);

        var subscription = await _builder.Build().GetSubscriptionAsync(999999999);

        Assert.Null(subscription);
    }

    [Fact]
    public async Task ReadsTheEndOfPeriodCancellationFlag()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription(cancelAtEndOfPeriod: true));

        var subscription = await _builder.Build().GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.True(subscription!.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 14, 48, 10, TimeSpan.FromHours(-5)),
            subscription.CurrentPeriodEndsAt);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("on_hold", SubscriptionState.OnHold)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("something_new", SubscriptionState.Unknown)]
    public async Task MapsProviderStatesOntoTheDomainEnum(string providerState, SubscriptionState expected)
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.OK,
            MaxioPayloads.Subscription(state: providerState));

        var subscription = await _builder.Build().GetSubscriptionAsync(15236915);

        Assert.Equal(expected, subscription!.State);
    }
}
