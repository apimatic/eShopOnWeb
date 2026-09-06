using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// In-memory stand-in for the Maxio API that reproduces the two behaviours this integration
/// depends on: single-resource lookups answer 404 when nothing matches, and a create is rejected
/// with 422 "Reference: must be unique" when the caller-assigned reference is already taken.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public MaxioSite? Site { get; set; } = new() { Currency = "USD", Subdomain = "test-site", Test = true };

    public List<MaxioProductFamily> ProductFamilies { get; } = new();

    public Dictionary<long, List<MaxioProduct>> ProductsByFamilyId { get; } = new();

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CustomerCreateCount { get; private set; }

    public int SubscriptionCreateCount { get; private set; }

    /// <summary>
    /// Runs before a subscription is created, so a test can inject the record a concurrent caller
    /// would have written and force the duplicate-reference path.
    /// </summary>
    public Action<MaxioCreateSubscription>? BeforeCreateSubscription { get; set; }

    public Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken) => Task.FromResult(Site);

    public Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioProductFamily>>(ProductFamilies);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(
            ProductsByFamilyId.TryGetValue(productFamilyId, out var products) ? products : new List<MaxioProduct>());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        CustomerCreateCount++;

        if (Customers.Any(c => c.Reference == customer.Reference))
        {
            throw DuplicateReference("POST", "customers.json");
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

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.FirstOrDefault(s => s.Reference == reference));

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        BeforeCreateSubscription?.Invoke(subscription);

        SubscriptionCreateCount++;

        if (Subscriptions.Any(s => s.Reference == subscription.Reference))
        {
            throw DuplicateReference("POST", "subscriptions.json");
        }

        var product = ProductsByFamilyId.Values
            .SelectMany(products => products)
            .FirstOrDefault(p => p.Handle == subscription.ProductHandle);

        var created = NewSubscription(
            reference: subscription.Reference,
            state: "active",
            product: product,
            customer: Customers.First(c => c.Id == subscription.CustomerId),
            paymentCollectionMethod: subscription.PaymentCollectionMethod);

        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public MaxioSubscription NewSubscription(
        string? reference,
        string state,
        MaxioProduct? product,
        MaxioCustomer customer,
        string? paymentCollectionMethod = "remittance")
    {
        var now = DateTimeOffset.UtcNow;

        return new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            Reference = reference,
            State = state,
            Product = product,
            Customer = customer,
            ProductPriceInCents = product?.PriceInCents ?? 0,
            BalanceInCents = product?.PriceInCents ?? 0,
            Currency = "USD",
            PaymentCollectionMethod = paymentCollectionMethod,
            CreatedAt = now,
            ActivatedAt = now,
            CurrentPeriodStartedAt = now,
            CurrentPeriodEndsAt = now.AddMonths(1),
            NextAssessmentAt = now.AddMonths(1)
        };
    }

    public void SeedFamily(string handle, long id, params MaxioProduct[] products)
    {
        ProductFamilies.Add(new MaxioProductFamily { Id = id, Handle = handle, Name = handle });

        foreach (var product in products)
        {
            product.ProductFamily = ProductFamilies[^1];
        }

        ProductsByFamilyId[id] = products.ToList();
    }

    private static MaxioApiException DuplicateReference(string method, string path) =>
        new(HttpStatusCode.UnprocessableEntity, method, path,
            new[] { "Reference: must be unique - that value has been taken." });
}
