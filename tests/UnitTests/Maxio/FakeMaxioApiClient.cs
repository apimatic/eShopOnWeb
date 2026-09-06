using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// An in-memory stand-in for the Maxio API that reproduces the behaviours the integration relies on:
/// customer references are unique, lookups 404 for unknown references, and subscriptions accumulate
/// against a customer.
/// </summary>
public sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly ConcurrentDictionary<string, MaxioCustomer> _customersByReference = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, List<MaxioSubscription>> _subscriptionsByCustomer = new();
    private int _nextId = 1000;

    public FakeMaxioApiClient(params MaxioProduct[] products)
    {
        Products = products.ToList();
    }

    public List<MaxioProduct> Products { get; }

    public MaxioSite Site { get; set; } = new()
    {
        Id = 1,
        Subdomain = "acme",
        Currency = "USD",
        RelationshipInvoicingEnabled = true,
        DefaultPaymentCollectionMethod = CollectionMethods.Automatic
    };

    public List<CreateSubscriptionRequest> CreateSubscriptionRequests { get; } = new();

    public int CreateCustomerCallCount;

    public int CreateSubscriptionCallCount;

    /// <summary>When set, the next customer create fails as if another caller had claimed the reference first.</summary>
    public bool SimulateCustomerReferenceRace { get; set; }

    /// <summary>Delay injected into every call so concurrent flows genuinely overlap.</summary>
    public TimeSpan CallLatency { get; set; } = TimeSpan.Zero;

    public Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default) => Delayed(Site);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyId, CancellationToken cancellationToken = default) =>
        Delayed<IReadOnlyList<MaxioProduct>>(Products.ToArray());

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Delayed(_customersByReference.TryGetValue(reference, out var customer) ? customer : null);

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateCustomerCallCount);
        await Latency();

        var reference = request.Customer.Reference ?? Guid.NewGuid().ToString();

        if (SimulateCustomerReferenceRace)
        {
            SimulateCustomerReferenceRace = false;
            SeedCustomer(reference, request.Customer.Email);

            throw new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "customers.json",
                new[] { "Reference: must be unique - that value has been taken." });
        }

        var customer = new MaxioCustomer
        {
            Id = Interlocked.Increment(ref _nextId),
            Reference = reference,
            Email = request.Customer.Email,
            FirstName = request.Customer.FirstName,
            LastName = request.Customer.LastName
        };

        if (!_customersByReference.TryAdd(reference, customer))
        {
            throw new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "customers.json",
                new[] { "Reference: must be unique - that value has been taken." });
        }

        return customer;
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = _subscriptionsByCustomer.TryGetValue(customerId, out var list)
            ? list.ToArray()
            : Array.Empty<MaxioSubscription>();

        return Delayed<IReadOnlyList<MaxioSubscription>>(subscriptions);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateSubscriptionCallCount);
        lock (CreateSubscriptionRequests)
        {
            CreateSubscriptionRequests.Add(request);
        }

        await Latency();

        var customerId = request.Subscription.CustomerId ?? 0;
        var product = Products.Single(candidate => candidate.Handle == request.Subscription.ProductHandle);
        var customer = _customersByReference.Values.FirstOrDefault(candidate => candidate.Id == customerId);

        var subscription = new MaxioSubscription
        {
            Id = Interlocked.Increment(ref _nextId),
            State = "active",
            Reference = request.Subscription.Reference,
            ProductPriceInCents = product.PriceInCents,
            Currency = Site.Currency,
            PaymentCollectionMethod = request.Subscription.PaymentCollectionMethod,
            Product = product,
            Customer = customer,
            CreatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            ActivatedAt = DateTimeOffset.UtcNow
        };

        _subscriptionsByCustomer.GetOrAdd(customerId, _ => new List<MaxioSubscription>());
        lock (_subscriptionsByCustomer[customerId])
        {
            _subscriptionsByCustomer[customerId].Add(subscription);
        }

        return subscription;
    }

    public MaxioCustomer SeedCustomer(string reference, string? email = null)
    {
        var customer = new MaxioCustomer
        {
            Id = Interlocked.Increment(ref _nextId),
            Reference = reference,
            Email = email ?? "seeded@example.com",
            FirstName = "Seeded",
            LastName = "Customer"
        };

        _customersByReference[reference] = customer;
        return customer;
    }

    public MaxioSubscription SeedSubscription(int customerId, MaxioProduct product, string state, string? reference = null)
    {
        var subscription = new MaxioSubscription
        {
            Id = Interlocked.Increment(ref _nextId),
            State = state,
            Reference = reference,
            Product = product,
            ProductPriceInCents = product.PriceInCents,
            Currency = Site.Currency,
            Customer = _customersByReference.Values.FirstOrDefault(candidate => candidate.Id == customerId),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _subscriptionsByCustomer.GetOrAdd(customerId, _ => new List<MaxioSubscription>()).Add(subscription);
        return subscription;
    }

    private async Task<T> Delayed<T>(T value)
    {
        await Latency();
        return value;
    }

    private Task Latency() => CallLatency > TimeSpan.Zero ? Task.Delay(CallLatency) : Task.CompletedTask;
}
