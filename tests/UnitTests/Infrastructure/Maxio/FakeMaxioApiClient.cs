using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// In-memory stand-in for Maxio that reproduces the one server behaviour the integration's
/// idempotency depends on: site-wide uniqueness of customer and subscription references, rejected
/// with the same 422 Maxio returns.
/// </summary>
internal class FakeMaxioApiClient : IMaxioApiClient
{
    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    // Records are stamped a second apart so "most recent first" ordering is deterministic in tests
    // rather than dependent on the host clock's granularity.
    private DateTimeOffset _clock = DateTimeOffset.UtcNow;

    public MaxioSite Site { get; set; } = new() { Currency = "USD", Subdomain = "test-site" };

    public List<MaxioProduct> Products { get; } = new();

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    /// <summary>Runs immediately before a create, so a test can simulate losing a race.</summary>
    public Action<MaxioSubscriptionAttributes>? BeforeCreateSubscription { get; set; }

    /// <summary>Runs immediately before a customer create, so a test can simulate losing a race.</summary>
    public Action<MaxioCustomerAttributes>? BeforeCreateCustomer { get; set; }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default) => Task.FromResult(Site);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(
            Products.Where(p => p.ProductFamily?.Handle == productFamilyHandle).ToList());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;
        BeforeCreateCustomer?.Invoke(customer);

        if (Customers.Any(c => c.Reference == customer.Reference))
        {
            throw ReferenceTaken();
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
        MaxioSubscriptionAttributes subscription,
        CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        BeforeCreateSubscription?.Invoke(subscription);

        if (Subscriptions.Any(s => s.Reference == subscription.Reference))
        {
            throw ReferenceTaken();
        }

        var customer = Customers.FirstOrDefault(c => c.Id == subscription.CustomerId)
            ?? throw new MaxioApiException("Customer not found", HttpStatusCode.NotFound);

        var product = Products.FirstOrDefault(p => p.Handle == subscription.ProductHandle)
            ?? throw new MaxioApiException("Product not found", HttpStatusCode.NotFound);

        var created = NewSubscription(subscription.Reference, "active", customer, product);
        created.PaymentCollectionMethod = subscription.PaymentCollectionMethod;

        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscriptions.FirstOrDefault(s => s.Reference == reference));

    public MaxioProduct AddProduct(string handle, string name, long priceInCents, string familyHandle, DateTimeOffset? archivedAt = null)
    {
        var product = new MaxioProduct
        {
            Id = Products.Count + 1,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Handle = familyHandle, Name = familyHandle }
        };

        Products.Add(product);
        return product;
    }

    public MaxioCustomer AddCustomer(string reference)
    {
        var customer = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            Reference = reference,
            Email = "shopper@example.com",
            FirstName = "Test",
            LastName = "Shopper",
            CreatedAt = DateTimeOffset.UtcNow
        };

        Customers.Add(customer);
        return customer;
    }

    public MaxioSubscription AddSubscription(MaxioCustomer customer, string productHandle, string state, string reference)
    {
        var product = Products.First(p => p.Handle == productHandle);
        var subscription = NewSubscription(reference, state, customer, product);

        Subscriptions.Add(subscription);
        return subscription;
    }

    private MaxioSubscription NewSubscription(string? reference, string state, MaxioCustomer customer, MaxioProduct product)
    {
        var createdAt = _clock = _clock.AddSeconds(1);

        return new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            Reference = reference,
            State = state,
            Currency = Site.Currency,
            ProductPriceInCents = product.PriceInCents,
            BalanceInCents = product.PriceInCents,
            CreatedAt = createdAt,
            ActivatedAt = createdAt,
            CurrentPeriodStartedAt = createdAt,
            CurrentPeriodEndsAt = createdAt.AddMonths(1),
            NextAssessmentAt = createdAt.AddMonths(1),
            Customer = customer,
            Product = product
        };
    }

    private static MaxioApiException ReferenceTaken() =>
        new(
            "Maxio request failed with 422 UnprocessableEntity",
            HttpStatusCode.UnprocessableEntity,
            new[] { "Reference: must be unique - that value has been taken." });
}
