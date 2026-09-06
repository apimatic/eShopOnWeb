using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// An in-memory stand-in for a Maxio site. It reproduces the two behaviours enrollment depends on:
/// customer references and subscription references are unique per site, and a create that collides
/// with one is rejected with the 422 the specification's error schema describes.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private static readonly IReadOnlyList<string> ReferenceTaken =
        new[] { "Reference: must be unique - that value has been taken." };

    private int _nextId = 1000;

    public MaxioSite Site { get; set; } = new()
    {
        Id = 1,
        Subdomain = "example-site",
        Currency = "USD",
        RelationshipInvoicingEnabled = true,
        Test = true
    };

    public List<MaxioProduct> Products { get; } = new();

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    /// <summary>Runs before the next create, to simulate another instance winning a race.</summary>
    public Action? BeforeCreateSubscription { get; set; }

    public MaxioProduct AddProduct(string handle, string name, long priceInCents)
    {
        var product = new MaxioProduct
        {
            Id = _nextId++,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = false,
            ProductPricePointName = "Original"
        };

        Products.Add(product);
        return product;
    }

    public MaxioSubscription AddSubscription(int customerId, string productHandle, string reference, string state)
    {
        var subscription = new MaxioSubscription
        {
            Id = _nextId++,
            State = state,
            Reference = reference,
            Currency = Site.Currency,
            PaymentCollectionMethod = "remittance",
            CreatedAt = DateTimeOffset.UtcNow,
            Customer = Customers.FirstOrDefault(c => c.Id == customerId),
            Product = Products.FirstOrDefault(p => p.Handle == productHandle)
        };

        subscription.ProductPriceInCents = subscription.Product?.PriceInCents ?? 0;
        Subscriptions.Add(subscription);
        return subscription;
    }

    public Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Site);

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));

    public Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls++;

        if (Customers.Any(c => c.Reference == customer.Reference))
        {
            throw Conflict(HttpMethod.Post, "customers.json");
        }

        var created = new MaxioCustomer
        {
            Id = _nextId++,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Reference = customer.Reference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Customers.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());

    public Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Subscriptions.FirstOrDefault(s => s.Reference == reference));

    public Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        BeforeCreateSubscription?.Invoke();
        BeforeCreateSubscription = null;

        CreateSubscriptionCalls++;

        if (Subscriptions.Any(s => s.Reference == subscription.Reference))
        {
            throw Conflict(HttpMethod.Post, "subscriptions.json");
        }

        var product = Products.FirstOrDefault(p => p.Handle == subscription.ProductHandle);
        var created = new MaxioSubscription
        {
            Id = _nextId++,
            State = "active",
            Reference = subscription.Reference,
            Currency = Site.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            ProductPriceInCents = product?.PriceInCents ?? 0,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            Customer = Customers.FirstOrDefault(c => c.Id == subscription.CustomerId),
            Product = product
        };

        Subscriptions.Add(created);
        return Task.FromResult(created);
    }

    private static MaxioApiException Conflict(HttpMethod method, string path) =>
        new(method, path, HttpStatusCode.UnprocessableEntity, ReferenceTaken);
}
