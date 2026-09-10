using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// In-memory fake of <see cref="IMaxioApiClient"/> that models Maxio's relevant semantics: unique customer
/// reference, customer-scoped subscriptions, and configurable failures. Lets us assert the orchestration
/// (idempotency, plan validation, race handling) without any network I/O.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly List<MaxioProduct> _products = new();
    private readonly List<MaxioCustomer> _customers = new();
    private readonly Dictionary<long, List<MaxioSubscription>> _subscriptionsByCustomer = new();
    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }

    /// <summary>When set, the next CreateCustomerAsync throws this before persisting (to simulate a 422 race).</summary>
    public Func<MaxioBillingException>? CreateCustomerThrows { get; set; }

    public FakeMaxioApiClient AddProduct(string handle, string name, long priceInCents = 1000,
        DateTimeOffset? archivedAt = null)
    {
        _products.Add(new MaxioProduct
        {
            Id = _products.Count + 1,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            ArchivedAt = archivedAt,
        });
        return this;
    }

    public MaxioCustomer SeedCustomer(string reference)
    {
        var customer = new MaxioCustomer { Id = _nextCustomerId++, Reference = reference };
        _customers.Add(customer);
        _subscriptionsByCustomer[customer.Id] = new List<MaxioSubscription>();
        return customer;
    }

    public MaxioSubscription SeedSubscription(long customerId, string productHandle, string state)
    {
        var product = _products.FirstOrDefault(p => p.Handle == productHandle)
            ?? new MaxioProduct { Handle = productHandle, Name = productHandle, PriceInCents = 1000, IntervalUnit = "month" };
        var subscription = new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            State = state,
            Product = product,
            ProductPriceInCents = product.PriceInCents,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        };
        _subscriptionsByCustomer[customerId].Add(subscription);
        return subscription;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MaxioProduct>>(_products.ToList());

    public Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer,
        CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;

        if (CreateCustomerThrows is not null)
        {
            var thrower = CreateCustomerThrows;
            CreateCustomerThrows = null; // one-shot
            throw thrower();
        }

        // Maxio enforces a unique reference.
        if (_customers.Any(c => c.Reference == customer.Reference))
        {
            throw new MaxioBillingException("reference has been taken", upstreamStatusCode: 422);
        }

        var created = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            Reference = customer.Reference,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
        };
        _customers.Add(created);
        _subscriptionsByCustomer[created.Id] = new List<MaxioSubscription>();
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default)
    {
        var list = _subscriptionsByCustomer.TryGetValue(customerId, out var subs)
            ? subs.ToList()
            : new List<MaxioSubscription>();
        return Task.FromResult<IReadOnlyList<MaxioSubscription>>(list);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription,
        CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        var created = SeedSubscription(subscription.CustomerId, subscription.ProductHandle!, "active");
        created.Reference = subscription.Reference;
        created.PaymentCollectionMethod = subscription.PaymentCollectionMethod;
        return Task.FromResult(created);
    }
}
