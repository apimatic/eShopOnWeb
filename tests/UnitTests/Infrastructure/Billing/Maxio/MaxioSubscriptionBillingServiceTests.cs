using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly Subscriber Demo = new("demouser@microsoft.com");

    private static MaxioSubscriptionBillingService Build(
        FakeMaxioApiClient client,
        MaxioOptions? options = null) =>
        new(client,
            new MaxioSiteCache(),
            new SubscriberLocks(),
            Options.Create(options ?? new MaxioOptions
            {
                ApiKey = "not-a-real-key",
                Subdomain = "example-site",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static FakeMaxioApiClient SeededCatalog()
    {
        var client = new FakeMaxioApiClient();
        client.AddProduct("eshop-pro", "Pro Plan", 29_900);
        client.AddProduct("basic-plan", "Basic Plan", 2_900);
        return client;
    }

    [Fact]
    public async Task ListsThePlansOfTheConfiguredFamilyCheapestFirst()
    {
        var service = Build(SeededCatalog());

        var plans = await service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.Equal("USD", plans.First().Currency);
        Assert.Equal(29m, plans.First().Price);
    }

    [Fact]
    public async Task HidesArchivedPlans()
    {
        var client = SeededCatalog();
        client.Products.Single(p => p.Handle == "basic-plan").ArchivedAt = System.DateTimeOffset.UtcNow;

        var plans = await Build(client).ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task SubscribingCreatesTheBillingCustomerAndTheSubscription()
    {
        var client = SeededCatalog();
        var service = Build(client);

        var result = await service.SubscribeAsync(Demo, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal("eshoponweb--demouser-microsoft-com", result.Subscription.CustomerReference);
        Assert.Equal("eshoponweb--demouser-microsoft-com--eshop-pro", result.Subscription.Reference);
        Assert.Equal(1, client.CreateCustomerCalls);
        Assert.Equal(1, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheSameSubscriptionAndCreatesNothingExtra()
    {
        var client = SeededCatalog();
        var service = Build(client);

        var first = await service.SubscribeAsync(Demo, "eshop-pro");
        var second = await service.SubscribeAsync(Demo, "eshop-pro");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, client.CreateCustomerCalls);
        Assert.Equal(1, client.CreateSubscriptionCalls);
        Assert.Single(client.Customers);
        Assert.Single(client.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentSubscribeAttemptsAllResolveToOneSubscription()
    {
        var client = SeededCatalog();
        var service = Build(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(Demo, "eshop-pro")));

        Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
        Assert.Single(client.Subscriptions);
        Assert.Single(client.Customers);
    }

    [Fact]
    public async Task ReusesAnExistingBillingCustomerInsteadOfCreatingASecondOne()
    {
        var client = SeededCatalog();
        await client.CreateCustomerAsync(new global::Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts.MaxioCreateCustomer
        {
            FirstName = "Demouser",
            LastName = "Customer",
            Email = "demouser@microsoft.com",
            Reference = Demo.CustomerReference
        });

        var service = Build(client);
        await service.SubscribeAsync(Demo, "eshop-pro");

        Assert.Single(client.Customers);
        Assert.Equal(1, client.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribingToASecondPlanCreatesASeparateSubscription()
    {
        var client = SeededCatalog();
        var service = Build(client);

        var pro = await service.SubscribeAsync(Demo, "eshop-pro");
        var basic = await service.SubscribeAsync(Demo, "basic-plan");

        Assert.True(pro.Created);
        Assert.True(basic.Created);
        Assert.NotEqual(pro.Subscription.Id, basic.Subscription.Id);
        Assert.Single(client.Customers);
        Assert.Equal(2, client.Subscriptions.Count);
    }

    [Fact]
    public async Task ResubscribingAfterCancellationTakesTheNextReferenceSlot()
    {
        var client = SeededCatalog();
        var service = Build(client);

        var first = await service.SubscribeAsync(Demo, "eshop-pro");
        client.Subscriptions.Single(s => s.Id == first.Subscription.Id).State = "canceled";

        var second = await service.SubscribeAsync(Demo, "eshop-pro");

        Assert.True(second.Created);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal("eshoponweb--demouser-microsoft-com--eshop-pro--2", second.Subscription.Reference);
    }

    [Fact]
    public async Task ARaceThatLosesTheReferenceResolvesToTheWinningSubscription()
    {
        var client = SeededCatalog();
        var service = Build(client);
        var customer = await client.CreateCustomerAsync(new global::Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts.MaxioCreateCustomer
        {
            FirstName = "Demouser",
            LastName = "Customer",
            Email = "demouser@microsoft.com",
            Reference = Demo.CustomerReference
        });

        // Another instance wins the reference in the window between the lookup and the create.
        client.BeforeCreateSubscription = () =>
            client.AddSubscription(customer.Id, "eshop-pro", Demo.SubscriptionReference("eshop-pro"), "active");

        var result = await service.SubscribeAsync(Demo, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(Demo.SubscriptionReference("eshop-pro"), result.Subscription.Reference);
        Assert.Single(client.Subscriptions);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheConfiguredFamily()
    {
        var service = Build(SeededCatalog());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Demo, "some-other-product"));
    }

    [Fact]
    public async Task ListsAShoppersSubscriptionsNewestFirst()
    {
        var client = SeededCatalog();
        var service = Build(client);

        await service.SubscribeAsync(Demo, "eshop-pro");
        await service.SubscribeAsync(Demo, "basic-plan");

        var subscriptions = await service.ListSubscriptionsAsync(Demo);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("basic-plan", subscriptions.First().PlanHandle);
        Assert.All(subscriptions, s => Assert.True(s.IsActive));
    }

    [Fact]
    public async Task ReportsNoSubscriptionsForAShopperWhoHasNeverSubscribed()
    {
        var subscriptions = await Build(SeededCatalog()).ListSubscriptionsAsync(Demo);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task NeverReturnsAnotherShoppersSubscriptions()
    {
        var client = SeededCatalog();
        var service = Build(client);
        await service.SubscribeAsync(Demo, "eshop-pro");

        var subscriptions = await service.ListSubscriptionsAsync(new Subscriber("admin@microsoft.com"));

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task EnrollsOnAnInvoiceStyleCollectionMethodBecauseNoCardIsEverCaptured()
    {
        var client = SeededCatalog();

        await Build(client).SubscribeAsync(Demo, "eshop-pro");
        Assert.Equal("remittance", client.Subscriptions.Single().PaymentCollectionMethod);

        var legacy = SeededCatalog();
        legacy.Site.RelationshipInvoicingEnabled = false;

        await Build(legacy).SubscribeAsync(Demo, "eshop-pro");
        Assert.Equal("invoice", legacy.Subscriptions.Single().PaymentCollectionMethod);
    }

    [Fact]
    public async Task HonoursAConfiguredCollectionMethodOverride()
    {
        var client = SeededCatalog();
        var options = new MaxioOptions
        {
            ApiKey = "not-a-real-key",
            Subdomain = "example-site",
            ProductFamilyHandle = "eshop-subscribe",
            PaymentCollectionMethod = "automatic"
        };

        await Build(client, options).SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal("automatic", client.Subscriptions.Single().PaymentCollectionMethod);
    }

    [Fact]
    public async Task ReportsMissingConfigurationByKeyNameInsteadOfFailingObscurely()
    {
        var service = Build(SeededCatalog(), new MaxioOptions());

        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => service.SubscribeAsync(Demo, "eshop-pro"));

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }
}
