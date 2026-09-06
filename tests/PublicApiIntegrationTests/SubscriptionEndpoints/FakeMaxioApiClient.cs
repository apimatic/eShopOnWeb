using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-memory stand-in for Maxio. It keeps the behaviour the integration depends on -
/// lookups by reference and the uniqueness constraint on reference values - so the endpoint
/// tests exercise the real idempotency logic without calling the sandbox.
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private int _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new()
    {
        new MaxioProduct
        {
            Id = 1,
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        },
        new MaxioProduct
        {
            Id = 2,
            Handle = "basic-plan",
            Name = "Basic Plan",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        }
    };

    public int CreatedCustomerCount { get; private set; }

    public int CreatedSubscriptionCount { get; private set; }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products);

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.FirstOrDefault(customer => customer.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        if (_customers.Any(existing => existing.Reference == customer.Reference))
        {
            throw DuplicateReference("POST", "/customers.json");
        }

        var created = new MaxioCustomer
        {
            Id = _nextId++,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Reference = customer.Reference,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _customers.Add(created);
        CreatedCustomerCount++;

        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            _subscriptions.Where(subscription => subscription.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_subscriptions.FirstOrDefault(subscription => subscription.Reference == reference));

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.Any(existing => existing.Reference == subscription.Reference))
        {
            throw DuplicateReference("POST", "/subscriptions.json");
        }

        var product = Products.Single(candidate => candidate.Handle == subscription.ProductHandle);
        var now = DateTimeOffset.UtcNow;

        var created = new MaxioSubscription
        {
            Id = _nextId++,
            State = "active",
            Reference = subscription.Reference,
            ProductPriceInCents = product.PriceInCents,
            Currency = "USD",
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = now,
            CurrentPeriodEndsAt = now.AddMonths(1),
            NextAssessmentAt = now.AddMonths(1),
            ActivatedAt = now,
            CreatedAt = now,
            Product = product,
            Customer = _customers.Single(customer => customer.Id == subscription.CustomerId)
        };

        _subscriptions.Add(created);
        CreatedSubscriptionCount++;

        return Task.FromResult(created);
    }

    private static MaxioApiException DuplicateReference(string method, string path) => new(
        System.Net.HttpStatusCode.UnprocessableEntity,
        method,
        path,
        new[] { "Reference: must be unique - that value has been taken." });
}
