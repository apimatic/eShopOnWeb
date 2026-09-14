using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace PublicApiIntegrationTests.SubscriptionsEndpoints;

/// <summary>
/// In-memory stand-in for the Maxio API used by the subscription endpoint tests so they run
/// without network access or credentials.
/// </summary>
internal class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly List<MaxioProduct> _catalog;
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private long _nextCustomerId = 100;
    private long _nextSubscriptionId = 500;

    public FakeMaxioApiClient(IEnumerable<MaxioProduct> catalog)
    {
        _catalog = catalog.ToList();
    }

    public IReadOnlyList<MaxioSubscription> Subscriptions => _subscriptions;

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string familyHandle, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MaxioProduct>>(_catalog.ToList());
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        return Task.FromResult(_customers.FirstOrDefault(c => string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken ct = default)
    {
        if (_customers.Any(c => string.Equals(c.Reference, customer.Reference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioApiException(422, "Reference: must be unique - that value has been taken.", new[] { "Reference: must be unique - that value has been taken." });
        }

        var created = new MaxioCustomer
        {
            Id = _nextCustomerId++,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Organization = customer.Organization,
            Reference = customer.Reference
        };
        _customers.Add(created);
        return Task.FromResult(created);
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken ct = default)
    {
        var customer = _customers.FirstOrDefault(c => string.Equals(c.Reference, subscription.CustomerReference, StringComparison.OrdinalIgnoreCase));
        if (customer == null)
        {
            throw new MaxioApiException(422, "Customer reference was not found.");
        }

        var product = _catalog.FirstOrDefault(p => string.Equals(p.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase));
        if (product == null)
        {
            throw new MaxioApiException(404, "Product not found.");
        }

        var created = new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            State = "active",
            ProductPriceInCents = product.PriceInCents,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            ActivatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            Customer = customer,
            Product = product
        };
        _subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default)
    {
        var result = _subscriptions.Where(s => s.Customer != null && s.Customer.Id == customerId).ToList();
        return Task.FromResult<IReadOnlyList<MaxioSubscription>>(result);
    }
}

internal static class FakeMaxioCatalog
{
    public static readonly MaxioProduct ProPlan = new()
    {
        Id = 7126957,
        Name = "Pro Plan",
        Handle = "eshop-pro",
        Description = "The Pro plan.",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Id = 3023074, Handle = "eshop-subscribe" }
    };

    public static readonly MaxioProduct BasicPlan = new()
    {
        Id = 7126958,
        Name = "Basic Plan",
        Handle = "basic-plan",
        Description = "The Basic plan.",
        PriceInCents = 2900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Id = 3023074, Handle = "eshop-subscribe" }
    };

    public static IReadOnlyList<MaxioProduct> Plans => new[] { ProPlan, BasicPlan };
}
