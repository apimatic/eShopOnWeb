using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

/// <summary>
/// In-memory stand-in for the Maxio Billing API used to test the subscription
/// service orchestration, including idempotency and race handling.
/// </summary>
internal class FakeMaxioClient : IMaxioClient
{
    public readonly Dictionary<string, MaxioCustomer> CustomersByReference = new();
    public readonly Dictionary<int, MaxioSubscription> SubscriptionsById = new();
    public readonly Dictionary<string, int> SubscriptionIdsByReference = new();

    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 500;
    private int _createSubscriptionCalls;

    public int CreateSubscriptionCalls => _createSubscriptionCalls;

    public int FailCreateWithReferenceConflictNTimes { get; set; }

    public Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("USD");

    public Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioProduct> products = new List<MaxioProduct>
        {
            new() { Id = 1, Name = "Basic Plan", Handle = "basic-plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
            new() { Id = 2, Name = "Pro Plan", Handle = "eshop-pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        };
        return Task.FromResult(products);
    }

    public Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        CustomersByReference.TryGetValue(reference, out var customer);
        return Task.FromResult(customer);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email,
        string reference, CancellationToken cancellationToken = default)
    {
        var customer = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference
        };
        CustomersByReference.Add(reference, customer);
        return Task.FromResult(customer);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId,
        string reference, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _createSubscriptionCalls);

        if (SubscriptionIdsByReference.ContainsKey(reference))
        {
            throw new MaxioApiException(422, "{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}");
        }
        if (FailCreateWithReferenceConflictNTimes > 0)
        {
            // Simulate a race where the peer request created the subscription
            // between our lookup and our create.
            FailCreateWithReferenceConflictNTimes--;
            SubscriptionIdsByReference[reference] = _nextSubscriptionId;
            SubscriptionsById[_nextSubscriptionId] = new MaxioSubscription
            {
                Id = _nextSubscriptionId,
                State = "active",
                ProductPriceInCents = 29900,
                Currency = "USD",
                Product = new MaxioProduct { Handle = productHandle, Name = "Pro Plan" }
            };
            throw new MaxioApiException(422, "{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}");
        }

        var id = _nextSubscriptionId++;
        var subscription = new MaxioSubscription
        {
            Id = id,
            State = "active",
            ProductPriceInCents = productHandle == "eshop-pro" ? 29900 : 2900,
            Currency = "USD",
            CurrentPeriodEndsAt = DateTime.UtcNow.AddMonths(1),
            Product = new MaxioProduct { Handle = productHandle, Name = productHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan" },
            Customer = new MaxioCustomer { Id = customerId }
        };
        SubscriptionsById[id] = subscription;
        SubscriptionIdsByReference[reference] = id;
        return Task.FromResult(subscription);
    }

    public Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        SubscriptionsById.TryGetValue(subscriptionId, out var subscription);
        return Task.FromResult(subscription);
    }

    public Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        if (SubscriptionIdsByReference.TryGetValue(reference, out var id))
        {
            return Task.FromResult<MaxioSubscription?>(SubscriptionsById[id]);
        }
        return Task.FromResult<MaxioSubscription?>(null);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioSubscription> subscriptions = SubscriptionsById.Values
            .Where(s => s.Customer?.Id == customerId).ToList();
        return Task.FromResult(subscriptions);
    }

    public Task<MaxioSubscription> CancelSubscriptionAsync(int subscriptionId, string message,
        CancellationToken cancellationToken = default)
    {
        SubscriptionsById[subscriptionId].State = "canceled";
        return Task.FromResult(SubscriptionsById[subscriptionId]);
    }
}

internal static class TestServiceFactory
{
    public static (MaxioSubscriptionService Service, FakeMaxioClient Maxio) Create()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"billing-tests-{Guid.NewGuid()}")
            .Options;
        var context = new CatalogContext(options);

        var maxio = new FakeMaxioClient();
        var service = new MaxioSubscriptionService(
            maxio,
            new EfRepository<BillingAccount>(context),
            new EfRepository<BillingAccount>(context),
            new EfRepository<UserSubscription>(context),
            new EfRepository<UserSubscription>(context),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new MaxioSettings
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioSubscriptionService>.Instance);
        return (service, maxio);
    }
}

public class MaxioSubscriptionServiceTests
{
    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionThenReturnsExistingOnRepeat()
    {
        var (service, maxio) = TestServiceFactory.Create();

        var first = await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "eshop-pro"));
        var second = await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "eshop-pro"));

        Assert.True(first.Created);
        Assert.Equal(1, maxio.CreateSubscriptionCalls);
        Assert.False(second.Created);
        Assert.Equal(first.SubscriptionId, second.SubscriptionId);
        Assert.Equal(299m, first.Price);
        Assert.Equal("active", first.State);
        Assert.NotNull(first.NextBillingDateUtc);
    }

    [Fact]
    public async Task SubscribeRaceLoserRecoversWinnerViaReferenceLookup()
    {
        var (service, maxio) = TestServiceFactory.Create();
        var first = await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "eshop-pro"));
        // Reset the fake so the reference is not yet taken for a second user,
        // and simulate the peer request winning the create race for user-2.
        maxio.SubscriptionIdsByReference.Clear();
        maxio.FailCreateWithReferenceConflictNTimes = 1;

        var loser = await service.SubscribeAsync(new SubscribeRequest("user-2", "user.2@example.com", "eshop-pro"));

        // The 422 reference conflict must be recovered via lookup, returning
        // the subscription Maxio already created for this reference.
        Assert.False(loser.Created);
        Assert.Equal(maxio.SubscriptionIdsByReference["eshop-web-sub:user-2:eshop-pro"],
            loser.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeUnknownPlanThrows()
    {
        var (service, _) = TestServiceFactory.Create();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "no-such-plan")));
    }

    [Fact]
    public async Task GetSubscriptionsForUserRefreshesStateFromBillingSystem()
    {
        var (service, maxio) = TestServiceFactory.Create();

        var created = await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "eshop-pro"));
        await maxio.CancelSubscriptionAsync(created.SubscriptionId, "test");

        var subscriptions = await service.GetSubscriptionsForUserAsync("user-1");

        var summary = Assert.Single(subscriptions);
        Assert.Equal("canceled", summary.State);
    }

    [Fact]
    public async Task CustomerIsReusedAcrossSubscriptions()
    {
        var (service, maxio) = TestServiceFactory.Create();

        await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "eshop-pro"));
        await service.SubscribeAsync(new SubscribeRequest("user-1", "user.1@example.com", "basic-plan"));

        var customer = Assert.Single(maxio.CustomersByReference.Values);
        Assert.Equal(2, maxio.SubscriptionsById.Count);
        Assert.Equal("user.1@example.com", customer.Email);
    }
}

public class MaxioSettingsTests
{
    [Fact]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "ignored",
            ProductFamilyHandle = "f",
            BaseUrl = "https://custom.example.com/api/"
        };

        Assert.Equal("https://custom.example.com/api", settings.GetApiBaseUrl());
    }

    [Fact]
    public void BaseUrlIsDerivedFromSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "f"
        };

        Assert.Equal("https://acme.chargify.com", settings.GetApiBaseUrl());
    }

    [Fact]
    public void ValidateThrowsWithoutApiKey()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            ProductFamilyHandle = "f"
        };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }
}