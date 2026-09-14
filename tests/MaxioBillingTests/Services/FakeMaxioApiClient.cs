using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.MaxioBillingTests.Services;

/// <summary>
/// An in-memory stand-in for Maxio that keeps the parts of its behaviour the integration relies on:
/// customer references and subscription references are unique, and violating either is refused with
/// the same 422 the real provider sends.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private readonly List<MaxioProduct> _products = new();
    private int _nextId = 1000;

    public FakeMaxioApiClient()
    {
        _products.Add(new MaxioProduct
        {
            Id = 1,
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Id = 9, Handle = "eshop-subscribe" }
        });
    }

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    /// <summary>Runs just before a create is applied, so a test can inject a race.</summary>
    public Func<Task>? BeforeCreateSubscription { get; set; }

    /// <summary>Runs just before a customer create is applied, so a test can inject a race.</summary>
    public Func<Task>? BeforeCreateCustomer { get; set; }

    public IReadOnlyList<MaxioSubscription> Subscriptions => _subscriptions;

    public Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new MaxioSite { Id = 1, Subdomain = "test-site", Currency = "USD" });

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(_products);

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.FirstOrDefault(customer => customer.Reference == reference));

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;

        if (BeforeCreateCustomer is not null)
        {
            await BeforeCreateCustomer();
        }

        if (_customers.Any(existing => existing.Reference == customer.Reference))
        {
            throw ReferenceTaken();
        }

        var created = new MaxioCustomer
        {
            Id = _nextId++,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Organization = customer.Organization,
            Reference = customer.Reference
        };

        _customers.Add(created);
        return created;
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            _subscriptions.Where(subscription => subscription.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_subscriptions.FirstOrDefault(subscription => subscription.Reference == reference));

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;

        if (BeforeCreateSubscription is not null)
        {
            await BeforeCreateSubscription();
        }

        if (_subscriptions.Any(existing => existing.Reference == subscription.Reference))
        {
            throw ReferenceTaken();
        }

        var product = _products.First(candidate => candidate.Handle == subscription.ProductHandle);

        var created = new MaxioSubscription
        {
            Id = _nextId++,
            State = "active",
            Reference = subscription.Reference,
            Currency = "USD",
            ProductPriceInCents = product.PriceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CreatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            ActivatedAt = DateTimeOffset.UtcNow,
            Customer = _customers.First(customer => customer.Id == subscription.CustomerId),
            Product = product
        };

        _subscriptions.Add(created);
        return created;
    }

    /// <summary>Adds a customer as if it had been created out of band.</summary>
    public MaxioCustomer SeedCustomer(string reference)
    {
        var customer = new MaxioCustomer { Id = _nextId++, Reference = reference, Email = "seeded@example.com" };
        _customers.Add(customer);
        return customer;
    }

    /// <summary>Adds a subscription as if it had been created out of band.</summary>
    public MaxioSubscription SeedSubscription(MaxioCustomer customer, string planHandle, string state, string reference)
    {
        var subscription = new MaxioSubscription
        {
            Id = _nextId++,
            State = state,
            Reference = reference,
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Customer = customer,
            Product = _products.First(product => product.Handle == planHandle)
        };

        _subscriptions.Add(subscription);
        return subscription;
    }

    private static BillingProviderException ReferenceTaken() =>
        new("The billing provider rejected the request.", 422,
            new[] { "Reference: must be unique - that value has been taken." });
}
