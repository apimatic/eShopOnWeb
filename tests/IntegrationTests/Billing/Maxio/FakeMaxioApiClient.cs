#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

/// <summary>
/// An in-memory stand-in for Maxio that behaves the way the real service does where it matters:
/// one customer per reference, ids assigned on create, and subscriptions readable back per customer.
/// Hooks let a test make a single write fail the way Maxio would.
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<(long CustomerId, MaxioSubscription Subscription)> _subscriptions = new();
    private long _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new();

    public MaxioSite Site { get; set; } = new()
    {
        Id = 1,
        Subdomain = "acme",
        Currency = "USD",
        Test = true,
        RelationshipInvoicingEnabled = true
    };

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    public List<string?> SubmittedSubscriptionTokens { get; } = new();

    public List<string?> SubmittedPaymentCollectionMethods { get; } = new();

    /// <summary>Called before each create-subscription; return an exception to make that call fail.</summary>
    public Func<int, Exception?>? OnCreateSubscription { get; set; }

    /// <summary>Called before each create-customer; return an exception to make that call fail.</summary>
    public Func<int, Exception?>? OnCreateCustomer { get; set; }

    public MaxioProduct AddProduct(string handle, string name, long priceInCents,
        bool requireCreditCard = false, DateTimeOffset? archivedAt = null)
    {
        var product = new MaxioProduct
        {
            Id = _nextId++,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = requireCreditCard,
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Id = 7, Handle = "family", Name = "Family" }
        };

        Products.Add(product);
        return product;
    }

    public MaxioCustomer SeedCustomer(string reference)
    {
        var customer = new MaxioCustomer
        {
            Id = _nextId++,
            Reference = reference,
            Email = "seed@example.com",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _customers.Add(customer);
        return customer;
    }

    public MaxioSubscription SeedSubscription(long customerId, string productHandle, string state)
    {
        var product = Products.FirstOrDefault(p => p.Handle == productHandle);
        var subscription = new MaxioSubscription
        {
            Id = _nextId++,
            State = state,
            ProductPriceInCents = product?.PriceInCents ?? 0,
            Product = product,
            Customer = _customers.FirstOrDefault(c => c.Id == customerId),
            CreatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1)
        };

        _subscriptions.Add((customerId, subscription));
        return subscription;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;

        var failure = OnCreateCustomer?.Invoke(CreateCustomerCalls);
        if (failure is not null)
        {
            throw failure;
        }

        if (_customers.Any(c => c.Reference == request.Customer.Reference))
        {
            throw new BillingValidationException("Reference: must be unique.");
        }

        var customer = new MaxioCustomer
        {
            Id = _nextId++,
            Reference = request.Customer.Reference,
            FirstName = request.Customer.FirstName,
            LastName = request.Customer.LastName,
            Email = request.Customer.Email,
            Organization = request.Customer.Organization,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            _subscriptions.Where(entry => entry.CustomerId == customerId)
                .Select(entry => entry.Subscription)
                .ToList());

    public Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls++;
        SubmittedSubscriptionTokens.Add(request.UniquenessToken);
        SubmittedPaymentCollectionMethods.Add(request.Subscription.PaymentCollectionMethod);

        var failure = OnCreateSubscription?.Invoke(CreateSubscriptionCalls);
        if (failure is not null)
        {
            throw failure;
        }

        return Task.FromResult(SeedSubscription(request.Subscription.CustomerId,
            request.Subscription.ProductHandle, "active"));
    }

    public Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<MaxioSite?>(Site);
}
