using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioBillingServiceTests
{
    private static MaxioBillingService CreateService(FakeMaxioHandler handler, MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FakeMaxioHandler.ProductFamilyHandle
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var client = new MaxioApiClient(httpClient, Options.Create(options));
        return new MaxioBillingService(
            client,
            Options.Create(options),
            NullLogger<MaxioBillingService>.Instance,
            new MaxioUserLockRegistry());
    }

    private static SubscribeCommand Command(string planHandle = "eshop-pro", string userReference = "user-1") =>
        new(userReference, "user-1@example.com", "First", "Last", planHandle);

    [Fact]
    public async Task ListPlansReturnsOnlyActiveProductsOfTheConfiguredFamily()
    {
        var service = CreateService(new FakeMaxioHandler());

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var plan = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.PaymentMethodRequired);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnFirstUse()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Command());

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(1, handler.CustomerCreateCalls);
        Assert.Equal(1, handler.SubscriptionCreateCalls);
        Assert.NotNull(result.NextBillingDate);
    }

    [Fact]
    public async Task SubscribeIsIdempotentForTheSameUserAndPlan()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var first = await service.SubscribeAsync(Command());
        var second = await service.SubscribeAsync(Command());

        Assert.Equal(first.SubscriptionId, second.SubscriptionId);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(1, handler.SubscriptionCreateCalls);
    }

    [Fact]
    public async Task SubscribeReusesTheMaxioCustomerAcrossPlans()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        await service.SubscribeAsync(Command("eshop-pro"));
        await service.SubscribeAsync(Command("basic-plan"));

        Assert.Equal(1, handler.CustomerCreateCalls);
        Assert.Equal(2, handler.SubscriptionCreateCalls);
    }

    [Fact]
    public async Task SubscribeDoesNotDuplicateWhenCustomerAlreadyExistsInMaxio()
    {
        var handler = new FakeMaxioHandler().WithExistingCustomer("user-1");
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Command());

        Assert.Equal(0, handler.CustomerCreateCalls);
        Assert.Equal(1, handler.SubscriptionCreateCalls);
        Assert.False(result.AlreadySubscribed);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationCreatesANewSubscription()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var first = await service.SubscribeAsync(Command());
        handler.SetSubscriptionState(first.SubscriptionId, "canceled");

        var second = await service.SubscribeAsync(Command());

        Assert.NotEqual(first.SubscriptionId, second.SubscriptionId);
        Assert.Equal("active", second.State);
        Assert.True(handler.SubscriptionCreateCalls >= 2);
    }

    [Fact]
    public async Task SubscribeWithUnknownPlanFails()
    {
        var service = CreateService(new FakeMaxioHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubscribeAsync(Command("not-a-plan")));
    }

    [Fact]
    public async Task ListSubscriptionsForUnknownUserReturnsEmpty()
    {
        var service = CreateService(new FakeMaxioHandler());

        var subscriptions = await service.ListSubscriptionsForUserAsync("nobody");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsMaxioRecordedSubscriptions()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);
        await service.SubscribeAsync(Command("eshop-pro"));
        await service.SubscribeAsync(Command("basic-plan"));

        var subscriptions = await service.ListSubscriptionsForUserAsync("user-1");

        Assert.Equal(2, subscriptions.Count);
        Assert.All(subscriptions, s => Assert.Equal("active", s.State));
    }
}

public class MaxioOptionsTests
{
    [Theory]
    [InlineData("US", "https://my-site.chargify.com")]
    [InlineData("EU", "https://my-site.ebilling.maxio.com")]
    public void ResolveBaseUrlDerivesFromSubdomainAndEnvironment(string environment, string expected)
    {
        var options = new MaxioOptions { Subdomain = "my-site", Environment = environment };

        Assert.Equal(expected, options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrlPrefersTheVerbatimBaseUrlOverride()
    {
        var options = new MaxioOptions
        {
            Subdomain = "other-site",
            BaseUrl = "https://override.example.com/"
        };

        Assert.Equal("https://override.example.com", options.ResolveBaseUrl());
    }
}
