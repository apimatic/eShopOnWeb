using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly Subscriber Demo = new(
        ExternalId: "demouser@microsoft.com",
        Email: "demouser@microsoft.com",
        FirstName: "Demouser",
        LastName: "Microsoft",
        Organization: "eShopOnWeb");

    [Fact]
    public async Task ListsOnlyLivePlansOrderedByPrice()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        client.Products.Add(Product("starter-plan", 2900));
        client.Products.Add(Product("retired-plan", 100, archived: true));

        var plans = await Build(client).GetPlansAsync();

        Assert.Equal(new[] { "starter-plan", "pro-plan" }, plans.Select(plan => plan.Handle));
    }

    [Fact]
    public async Task CreatesTheCustomerAndTheSubscriptionOnAFirstSubscribe()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));

        var result = await Build(client).SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("pro-plan", result.Subscription.PlanHandle);
        Assert.Equal(1, client.CreatedCustomers.Count);
        Assert.Equal(1, client.CreatedSubscriptions.Count);

        // The reference is what ties the eShopOnWeb user to their billing customer.
        Assert.Equal("eshop-demouser@microsoft.com", client.CreatedCustomers[0].Reference);
    }

    [Fact]
    public async Task PassesTheConfiguredCollectionMethodSoEnrollmentNeedsNoPaymentMethod()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));

        await Build(client).SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));

        Assert.Equal(CollectionMethods.Remittance, client.CreatedSubscriptions[0].PaymentCollectionMethod);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        var service = Build(client);

        var first = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));
        var second = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, client.CreatedCustomers.Count);
        Assert.Equal(1, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task CollapsesConcurrentSubscribesForTheSameShopperIntoOneEnrollment()
    {
        var client = new FakeMaxioApiClient { LatencyMilliseconds = 15 };
        client.Products.Add(Product("pro-plan", 29900));
        var service = Build(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"))));

        Assert.Equal(1, client.CreatedCustomers.Count);
        Assert.Equal(1, client.CreatedSubscriptions.Count);
        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task LetsAShopperHoldSubscriptionsToDifferentPlans()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        client.Products.Add(Product("starter-plan", 2900));
        var service = Build(client);

        await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));
        var second = await service.SubscribeAsync(new SubscribeCommand(Demo, "starter-plan"));

        Assert.True(second.Created);
        Assert.Equal(1, client.CreatedCustomers.Count);
        Assert.Equal(2, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task LetsAShopperReSubscribeAfterCancelling()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        var service = Build(client);

        var first = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));
        client.Subscriptions.Single(s => s.Id == first.Subscription.Id).State = "canceled";

        var second = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));

        Assert.True(second.Created);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
    }

    [Fact]
    public async Task ReplaysAnIdempotencyKeyToTheSubscriptionItAlreadyProduced()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        var service = Build(client);

        var first = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan", "order-42"));
        var replay = await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan", "order-42"));

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal("order-42", replay.Subscription.Reference);
        Assert.Equal(1, client.CreatedSubscriptions.Count);
    }

    [Fact]
    public async Task RecoversWhenAnotherHostWinsTheRaceToCreateTheCustomer()
    {
        var client = new FakeMaxioApiClient { FailNextCustomerCreationAfterAnotherHostWins = true };
        client.Products.Add(Product("pro-plan", 29900));

        var result = await Build(client).SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal(1, client.CreatedCustomers.Count);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheCatalog()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => Build(client).SubscribeAsync(new SubscribeCommand(Demo, "no-such-plan")));

        Assert.Equal("no-such-plan", exception.PlanHandle);
        Assert.Empty(client.CreatedCustomers);
    }

    [Fact]
    public async Task TranslatesAValidationRejectionIntoADomainFailure()
    {
        var client = new FakeMaxioApiClient { SubscriptionCreationErrors = new[] { "Product: is archived." } };
        client.Products.Add(Product("pro-plan", 29900));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingValidationException>(
            () => Build(client).SubscribeAsync(new SubscribeCommand(Demo, "pro-plan")));

        Assert.Contains("is archived", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReadingSubscriptionsNeverCreatesACustomer()
    {
        var client = new FakeMaxioApiClient();

        var result = await Build(client).GetSubscriptionsAsync(Demo);

        Assert.Empty(result.Subscriptions);
        Assert.Null(result.CustomerId);
        Assert.Equal("eshop-demouser@microsoft.com", result.CustomerReference);
        Assert.Empty(client.CreatedCustomers);
    }

    [Fact]
    public async Task ReportsTheShoppersSubscriptionsNewestFirst()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("pro-plan", 29900));
        client.Products.Add(Product("starter-plan", 2900));
        var service = Build(client);

        await service.SubscribeAsync(new SubscribeCommand(Demo, "pro-plan"));
        await service.SubscribeAsync(new SubscribeCommand(Demo, "starter-plan"));

        var result = await service.GetSubscriptionsAsync(Demo);

        Assert.Equal(new[] { "starter-plan", "pro-plan" }, result.Subscriptions.Select(s => s.PlanHandle));
        Assert.NotNull(result.CustomerId);
    }

    [Fact]
    public async Task FailsWithNamedKeysWhenBillingIsNotConfigured()
    {
        var service = Build(new FakeMaxioApiClient(), new MaxioSettings());

        var exception = await Assert.ThrowsAsync<SubscriptionBillingConfigurationException>(
            () => service.GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    private static MaxioSubscriptionBillingService Build(FakeMaxioApiClient client, MaxioSettings? settings = null) =>
        new(
            client,
            new StaticOptionsMonitor<MaxioSettings>(settings ?? MaxioTestFactory.Settings()),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLoggerFactory.CreateLogger<MaxioSubscriptionBillingService>());

    private static MaxioProduct Product(string handle, long priceInCents, bool archived = false) => new()
    {
        Id = handle.GetHashCode() & 0x7fffffff,
        Handle = handle,
        Name = handle,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archived ? DateTimeOffset.UtcNow : null
    };
}

/// <summary>
/// An in-memory stand-in for Maxio that keeps the invariants the real service has: customer
/// references are unique, and every write is observable.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private int _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new();

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public List<MaxioCreateCustomer> CreatedCustomers { get; } = new();

    public List<MaxioCreateSubscription> CreatedSubscriptions { get; } = new();

    /// <summary>Artificial latency, which widens the window a race would slip through.</summary>
    public int LatencyMilliseconds { get; set; }

    /// <summary>Simulates another host creating the same customer between the lookup and the create.</summary>
    public bool FailNextCustomerCreationAfterAnotherHostWins { get; set; }

    public IReadOnlyList<string>? SubscriptionCreationErrors { get; set; }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        return Products.ToList();
    }

    public async Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (Customers)
        {
            return Customers.FirstOrDefault(customer => customer.Reference == reference);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();

        if (FailNextCustomerCreationAfterAnotherHostWins)
        {
            FailNextCustomerCreationAfterAnotherHostWins = false;
            lock (Customers)
            {
                Customers.Add(new MaxioCustomer { Id = Interlocked.Increment(ref _nextId), Reference = customer.Reference, Email = customer.Email });
                CreatedCustomers.Add(customer);
            }

            throw new MaxioApiException(
                "createCustomer",
                HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." });
        }

        lock (Customers)
        {
            if (Customers.Any(existing => existing.Reference == customer.Reference))
            {
                throw new MaxioApiException(
                    "createCustomer",
                    HttpStatusCode.UnprocessableEntity,
                    new[] { "Reference: must be unique - that value has been taken." });
            }

            var created = new MaxioCustomer
            {
                Id = Interlocked.Increment(ref _nextId),
                Reference = customer.Reference,
                Email = customer.Email,
                FirstName = customer.FirstName,
                LastName = customer.LastName
            };

            Customers.Add(created);
            CreatedCustomers.Add(customer);
            return created;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (Subscriptions)
        {
            return Subscriptions.Where(subscription => subscription.Customer?.Id == customerId).ToList();
        }
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (Subscriptions)
        {
            return Subscriptions.FirstOrDefault(subscription => subscription.Reference == reference);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        await DelayAsync();

        if (SubscriptionCreationErrors is { Count: > 0 })
        {
            throw new MaxioApiException("createSubscription", HttpStatusCode.UnprocessableEntity, SubscriptionCreationErrors);
        }

        var product = Products.First(candidate => candidate.Handle == subscription.ProductHandle);

        lock (Subscriptions)
        {
            var created = new MaxioSubscription
            {
                Id = Interlocked.Increment(ref _nextId),
                State = "active",
                Reference = subscription.Reference,
                ProductPriceInCents = product.PriceInCents,
                CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow.AddMilliseconds(Subscriptions.Count),
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                Product = product,
                Customer = Customers.First(customer => customer.Id == subscription.CustomerId)
            };

            Subscriptions.Add(created);
            CreatedSubscriptions.Add(subscription);
            return created;
        }
    }

    private Task DelayAsync() =>
        LatencyMilliseconds > 0 ? Task.Delay(LatencyMilliseconds) : Task.CompletedTask;
}
