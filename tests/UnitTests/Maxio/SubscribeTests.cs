using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio;
using Microsoft.eShopWeb.Maxio.Configuration;
using Microsoft.eShopWeb.Maxio.Contracts;
using Microsoft.eShopWeb.Maxio.Http;
using Microsoft.eShopWeb.Maxio.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class SubscribeTests
{
    private static MaxioOptions CreateOptions() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe",
        Environment = "US"
    };

    private static SubscribeCommand Command(string reference, string planHandle = "eshop-pro") => new()
    {
        CustomerReference = reference,
        Email = $"{reference}@example.com",
        FirstName = "Demo",
        LastName = "User",
        ProductHandle = planHandle
    };

    private async Task<MaxioCustomer> SeedCustomerAsync(FakeMaxioApiClient client, string reference)
    {
        return await client.CreateCustomerAsync(new MaxioNewCustomer
        {
            FirstName = "Demo",
            LastName = "User",
            Email = $"{reference}@example.com",
            Reference = reference
        }, CancellationToken.None);
    }

    private static MaxioSubscription SubscriptionFor(MaxioCustomer customer, string planHandle, string state)
    {
        bool isPro = planHandle == "eshop-pro";
        return new MaxioSubscription
        {
            Id = 9000 + (isPro ? 1 : 2),
            State = state,
            Product = new MaxioProduct
            {
                Id = isPro ? 1 : 2,
                Name = isPro ? "Pro Plan" : "Basic Plan",
                Handle = planHandle,
                PriceInCents = isPro ? 29900 : 2900,
                Interval = 1,
                IntervalUnit = "month"
            },
            Customer = customer,
            ProductPriceInCents = isPro ? 29900 : 2900,
            Currency = "USD",
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnRemittanceTerms()
    {
        var client = new FakeMaxioApiClient();
        var service = new MaxioBillingService(client, CreateOptions());

        var result = await service.SubscribeAsync(Command("new-customer"), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);

        var customer = Assert.Single(client.CreatedCustomers);
        Assert.Equal("new-customer", customer.Reference);
        Assert.Equal("new-customer@example.com", customer.Email);

        var subscription = Assert.Single(client.CreatedSubscriptions);
        Assert.Equal("eshop-pro", subscription.Product?.Handle);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeReturnsExistingActiveSubscriptionWithoutCreatingAnother()
    {
        var client = new FakeMaxioApiClient();
        var customer = await SeedCustomerAsync(client, "already-active");
        client.SeedSubscription(SubscriptionFor(customer, "eshop-pro", "active"));
        var service = new MaxioBillingService(client, CreateOptions());

        var result = await service.SubscribeAsync(Command("already-active"), CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Single(client.CreatedCustomers);
        Assert.Single(client.CreatedSubscriptions);
    }

    [Fact]
    public async Task SubscribeCreatesNewSubscriptionWhenOnlyCanceledOneExists()
    {
        var client = new FakeMaxioApiClient();
        var customer = await SeedCustomerAsync(client, "canceled-before");
        client.SeedSubscription(SubscriptionFor(customer, "eshop-pro", "canceled"));
        var service = new MaxioBillingService(client, CreateOptions());

        var result = await service.SubscribeAsync(Command("canceled-before"), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(2, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task SubscribeDoesNotTreatSubscriptionsToOtherPlansAsTheRequestedPlan()
    {
        var client = new FakeMaxioApiClient();
        var customer = await SeedCustomerAsync(client, "basic-holder");
        client.SeedSubscription(SubscriptionFor(customer, "basic-plan", "active"));
        var service = new MaxioBillingService(client, CreateOptions());

        var result = await service.SubscribeAsync(Command("basic-holder", "eshop-pro"), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(2, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task SubscribeThrowsWhenPlanIsNotInConfiguredFamily()
    {
        var client = new FakeMaxioApiClient();
        var service = new MaxioBillingService(client, CreateOptions());

        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            service.SubscribeAsync(Command("u", "not-in-family"), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentSubscribesToTheSamePlanCreateOneCustomerAndOneSubscription()
    {
        var client = new FakeMaxioApiClient();
        var service = new MaxioBillingService(client, CreateOptions());
        var command = Command("double-click", "eshop-pro");

        var results = await Task.WhenAll(
            service.SubscribeAsync(command, CancellationToken.None),
            service.SubscribeAsync(command, CancellationToken.None));

        Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Where(r => !r.Created));
        Assert.Single(client.CreatedCustomers);
        Assert.Single(client.CreatedSubscriptions);
    }
}
