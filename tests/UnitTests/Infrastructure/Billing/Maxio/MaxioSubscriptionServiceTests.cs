using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionServiceTests
{
    [Fact]
    public async Task GetPlansAsync_ReturnsActivePlansFromTheConfiguredFamily_CheapestFirst()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.Products.Add(MaxioTestBuilder.Product(
            "retired-plan", "Retired Plan", 100, archivedAt: DateTimeOffset.UtcNow.AddDays(-1)));

        var plans = await MaxioTestBuilder.Service(client).GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("USD", plans[0].Currency);
        Assert.Equal(new BillingInterval(1, BillingIntervalUnit.Month), plans[0].Interval);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheBillingCustomerAndTheSubscription()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();

        var result = await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(SubscriptionState.Active, result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customer = Assert.Single(client.Customers);
        Assert.Equal("eshoponweb:demouser@microsoft.com", customer.Reference);
        Assert.Single(client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_SendsAUniquenessTokenAndAnInvoiceCollectionMethod()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();

        await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro"));

        var sent = Assert.IsType<MaxioCreateSubscriptionAttributes>(client.LastCreateSubscription);
        Assert.False(string.IsNullOrWhiteSpace(sent.UniquenessToken));
        Assert.Equal("eshop-pro", sent.ProductHandle);
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:1", sent.Reference);

        // Relationship Invoicing site and no captured card, so signup must not try to charge one.
        Assert.Equal("remittance", sent.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_OnAStatementBasedSite_UsesInvoiceCollection()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.Site = client.Site with { RelationshipInvoicingEnabled = false };

        await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro"));

        Assert.Equal("invoice", client.LastCreateSubscription!.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_CalledTwice_DoesNotEnrollTwice()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        var service = MaxioTestBuilder.Service(client);
        var request = new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro");

        var first = await service.SubscribeAsync(request);
        var second = await service.SubscribeAsync(request);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(client.Subscriptions);
        Assert.Equal(1, client.CreateSubscriptionCalls);
        Assert.Equal(1, client.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAsync_ConcurrentDoubleClick_EnrollsExactlyOnce()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        var service = MaxioTestBuilder.Service(client);
        var subscriber = MaxioTestBuilder.Subscriber();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                service.SubscribeAsync(new SubscribeRequest(subscriber, "eshop-pro"))));

        Assert.Single(client.Customers);
        Assert.Single(client.Subscriptions);
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.All(results, r => Assert.Equal(client.Subscriptions[0].Id.ToString(), r.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheProviderCallsItADuplicate_ReconcilesInsteadOfFailing()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();

        // An earlier attempt succeeded at the provider but its reply was lost, so this one comes
        // back 409. The subscription exists; the caller should get it, not an error.
        client.SimulateLostSuccessOnNextCreate = true;

        var result = await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro", "key-1"));

        Assert.False(result.Created);
        Assert.Single(client.Subscriptions);
        Assert.Equal(client.Subscriptions[0].Id.ToString(), result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_WhenADuplicateCannotBeReconciled_SaysSoRatherThanRetrying()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.SimulateSpuriousDuplicateOnNextCreate = true;

        var exception = await Assert.ThrowsAsync<DuplicateBillingRequestException>(() =>
            MaxioTestBuilder.Service(client)
                .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro")));

        Assert.Contains("my-subscriptions", exception.Message);
        Assert.Empty(client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_AfterCancellation_EnrollsAgainWithAFreshReference()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        var service = MaxioTestBuilder.Service(client);
        var subscriber = MaxioTestBuilder.Subscriber();

        await service.SubscribeAsync(new SubscribeRequest(subscriber, "eshop-pro"));
        client.Subscriptions[0] = client.Subscriptions[0] with
        {
            State = "canceled",
            CanceledAt = DateTimeOffset.UtcNow
        };

        var resubscribed = await service.SubscribeAsync(new SubscribeRequest(subscriber, "eshop-pro"));

        Assert.True(resubscribed.Created);
        Assert.Equal(2, client.Subscriptions.Count);

        // The generation is part of the reference and the uniqueness token, so a genuine
        // re-subscribe is not mistaken for a replay of the original signup.
        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:2", resubscribed.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesAnExistingBillingCustomer()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.Customers.Add(new MaxioCustomer
        {
            Id = 77,
            Reference = "eshoponweb:demouser@microsoft.com",
            Email = "demouser@microsoft.com",
            FirstName = "Demo",
            LastName = "User"
        });

        var result = await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro"));

        Assert.Equal(0, client.CreateCustomerCalls);
        Assert.Equal("77", result.Subscription.CustomerId);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheCustomerIsCreatedConcurrently_ReusesTheWinner()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.OnBeforeCreateCustomer = attributes => new MaxioCustomer
        {
            Id = 4242,
            Reference = attributes.Reference,
            Email = attributes.Email,
            FirstName = attributes.FirstName,
            LastName = attributes.LastName
        };

        var result = await MaxioTestBuilder.Service(client)
            .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro"));

        Assert.Equal("4242", result.Subscription.CustomerId);
        Assert.Single(client.Customers);
    }

    [Fact]
    public async Task SubscribeAsync_WithAnUnknownPlan_Throws()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            MaxioTestBuilder.Service(client)
                .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "no-such-plan")));

        Assert.Equal("no-such-plan", exception.PlanHandle);
        Assert.Empty(client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_WhenThePlanNeedsAStoredCard_FailsBeforeCallingTheProvider()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        client.Products.Add(MaxioTestBuilder.Product("card-plan", "Card Plan", 1000, requireCreditCard: true));

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            MaxioTestBuilder.Service(client)
                .SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "card-plan")));

        Assert.Equal(0, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_WithoutConfiguration_ReportsWhatIsMissing()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        var service = MaxioTestBuilder.Service(client, new Microsoft.eShopWeb.Infrastructure.Billing.Maxio.MaxioOptions());

        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(() =>
            service.SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber(), "eshop-pro")));

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ForSomeoneWhoNeverSubscribed_IsEmpty()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();

        var subscriptions = await MaxioTestBuilder.Service(client)
            .GetSubscriptionsAsync(MaxioTestBuilder.Subscriber("nobody@example.com"));

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsOnlyTheCallersOwnSubscriptions()
    {
        var client = MaxioTestBuilder.ClientWithDefaultCatalog();
        var service = MaxioTestBuilder.Service(client);

        await service.SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber("alice@example.com"), "eshop-pro"));
        await service.SubscribeAsync(new SubscribeRequest(MaxioTestBuilder.Subscriber("bob@example.com"), "basic-plan"));

        var alice = await service.GetSubscriptionsAsync(MaxioTestBuilder.Subscriber("alice@example.com"));

        var only = Assert.Single(alice);
        Assert.Equal("eshop-pro", only.PlanHandle);
        Assert.Equal("eshoponweb:alice@example.com", only.CustomerReference);
        Assert.Equal(2, client.Customers.Count);
    }
}
