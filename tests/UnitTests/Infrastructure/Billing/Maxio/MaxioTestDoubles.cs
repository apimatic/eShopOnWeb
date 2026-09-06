using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// A stand-in for the Maxio API that behaves the way the real one does on the points the
/// subscribe flow depends on: one customer per reference, and a uniqueness token that can only be
/// used once.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public List<MaxioProduct> Products { get; } = new();
    public List<MaxioCustomer> Customers { get; } = new();
    public List<MaxioSubscription> Subscriptions { get; } = new();
    public HashSet<string> UsedUniquenessTokens { get; } = new(StringComparer.Ordinal);

    public MaxioSite Site { get; set; } = new()
    {
        Id = 1,
        Subdomain = "test-site",
        Currency = "USD",
        RelationshipInvoicingEnabled = true,
        Test = true
    };

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }
    public MaxioCreateSubscriptionAttributes? LastCreateSubscription { get; private set; }

    /// <summary>Simulates a concurrent create landing between our lookup and our own create.</summary>
    public Func<MaxioCustomerAttributes, MaxioCustomer?>? OnBeforeCreateCustomer { get; set; }

    /// <summary>
    /// Simulates the awkward case duplicate prevention exists for: the create succeeds at the
    /// provider, but the caller never sees the reply and retries into a 409.
    /// </summary>
    public bool SimulateLostSuccessOnNextCreate { get; set; }

    /// <summary>Makes the next create answer 409 without having created anything.</summary>
    public bool SimulateSpuriousDuplicateOnNextCreate { get; set; }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default) => Task.FromResult(Site);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(
            Products.Where(p => p.ProductFamily?.Handle == productFamilyHandle).ToList());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer,
        CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;

        var raced = OnBeforeCreateCustomer?.Invoke(customer);
        if (raced is not null)
        {
            Customers.Add(raced);
        }

        if (Customers.Any(c => c.Reference == customer.Reference))
        {
            throw new MaxioApiException(
                System.Net.Http.HttpMethod.Post,
                "customers.json",
                System.Net.HttpStatusCode.UnprocessableEntity,
                new[] { "reference: has already been taken" });
        }

        var created = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Reference = customer.Reference,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Customers.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionAttributes subscription,
        CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        LastCreateSubscription = subscription;

        if (SimulateLostSuccessOnNextCreate)
        {
            SimulateLostSuccessOnNextCreate = false;
            Materialize(subscription);
            throw Duplicate();
        }

        if (SimulateSpuriousDuplicateOnNextCreate)
        {
            SimulateSpuriousDuplicateOnNextCreate = false;
            throw Duplicate();
        }

        if (subscription.UniquenessToken is { } token && !UsedUniquenessTokens.Add(token))
        {
            throw new MaxioApiException(
                System.Net.Http.HttpMethod.Post,
                "subscriptions.json",
                System.Net.HttpStatusCode.Conflict,
                new[] { "DuplicatePrevention::DuplicateSubmissionError" });
        }

        return Task.FromResult(Materialize(subscription));
    }

    private MaxioSubscription Materialize(MaxioCreateSubscriptionAttributes subscription)
    {
        var product = Products.First(p => p.Handle == subscription.ProductHandle);
        var customer = Customers.First(c => c.Id == subscription.CustomerId);

        var created = new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            State = "active",
            Reference = subscription.Reference,
            ProductPriceInCents = product.PriceInCents,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAt = DateTimeOffset.UtcNow,
            Customer = customer,
            Product = product
        };

        Subscriptions.Add(created);
        return created;
    }

    private static MaxioApiException Duplicate() => new(
        System.Net.Http.HttpMethod.Post,
        "subscriptions.json",
        System.Net.HttpStatusCode.Conflict,
        new[] { "DuplicatePrevention::DuplicateSubmissionError" });
}

internal static class MaxioTestBuilder
{
    public const string FamilyHandle = "eshop-subscribe";

    public static SubscriberIdentity Subscriber(string userName = "demouser@microsoft.com") =>
        new(userName, userName);

    public static MaxioProduct Product(
        string handle,
        string name,
        long priceInCents,
        bool requireCreditCard = false,
        DateTimeOffset? archivedAt = null) => new()
    {
        Id = handle.Length,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = requireCreditCard,
        ArchivedAt = archivedAt,
        ProductFamily = new MaxioProductFamily { Id = 1, Handle = FamilyHandle, Name = "eShop Subscribe" }
    };

    public static MaxioSubscriptionService Service(FakeMaxioApiClient client, MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        };

        return new MaxioSubscriptionService(
            client,
            new StaticOptionsMonitor<MaxioOptions>(options),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    public static FakeMaxioApiClient ClientWithDefaultCatalog()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(Product("basic-plan", "Basic Plan", 2900));
        client.Products.Add(Product("eshop-pro", "Pro Plan", 29900));
        return client;
    }
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
