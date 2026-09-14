using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// An in-memory stand-in for Maxio that enforces the two constraints the idempotency design leans on:
/// customer and subscription references are unique per site, and a duplicate is rejected with the same
/// HTTP 422 the real API returns.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked because the interesting tests run calls concurrently, and they need
/// the fake to behave like a shared store with real uniqueness rather than like a script of replies.
/// </remarks>
public class FakeMaxioApiClient : IMaxioApiClient
{
    public const string ProductFamilyHandle = "test-family";
    public const string ProPlanHandle = "test-pro";
    public const string BasicPlanHandle = "test-basic";

    private readonly object _gate = new();
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private long _nextId = 1000;

    public ConcurrentBag<MaxioCreateSubscription> CreateSubscriptionCalls { get; } = new();

    public ConcurrentBag<MaxioCreateCustomer> CreateCustomerCalls { get; } = new();

    /// <summary>Latency injected into every call, to widen the window concurrency tests race through.</summary>
    public TimeSpan CallLatency { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Runs just before a create is applied, so a test can slip a competing write in and force the
    /// duplicate-reference rejection that a second application instance would cause.
    /// </summary>
    public Action<MaxioCreateSubscription>? OnBeforeCreateSubscription { get; set; }

    /// <summary>When set, <see cref="ListProductsForFamilyAsync"/> returns this instead of the default two plans.</summary>
    public IReadOnlyList<MaxioProduct>? Products { get; set; }

    public MaxioSite Site { get; set; } = new() { Id = 1, Subdomain = "test-site", Currency = "USD", Test = true };

    public MaxioProductFamily? ProductFamily { get; set; } = new()
    {
        Id = 500,
        Handle = ProductFamilyHandle,
        Name = "Test Family"
    };

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        return Site;
    }

    public async Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        return string.Equals(ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase) ? ProductFamily : null;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        return CatalogProducts;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_gate)
        {
            return _customers.FirstOrDefault(c => string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        CreateCustomerCalls.Add(customer);
        await DelayAsync();

        lock (_gate)
        {
            if (_customers.Any(c => string.Equals(c.Reference, customer.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw DuplicateReference("POST", "customers.json");
            }

            var created = new MaxioCustomer
            {
                Id = _nextId++,
                Reference = customer.Reference,
                Email = customer.Email,
                FirstName = customer.FirstName,
                LastName = customer.LastName
            };

            _customers.Add(created);
            return created;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_gate)
        {
            return _subscriptions.Where(s => s.Customer?.Id == customerId).ToList();
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        CreateSubscriptionCalls.Add(subscription);
        await DelayAsync();
        OnBeforeCreateSubscription?.Invoke(subscription);

        lock (_gate)
        {
            if (_subscriptions.Any(s => string.Equals(s.Reference, subscription.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw DuplicateReference("POST", "subscriptions.json");
            }

            var customer = _customers.FirstOrDefault(c => c.Id == subscription.CustomerId)
                           ?? throw new InvalidOperationException($"No fake customer with id {subscription.CustomerId}.");

            var product = CatalogProducts
                .FirstOrDefault(p => string.Equals(p.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new MaxioApiException(
                    HttpStatusCode.UnprocessableEntity,
                    "POST",
                    "subscriptions.json",
                    new[] { $"Product with API Handle '{subscription.ProductHandle}' does not exist for this site." });

            var now = DateTimeOffset.UtcNow;
            var created = new MaxioSubscription
            {
                Id = _nextId++,
                State = "active",
                Reference = subscription.Reference,
                Currency = Site.Currency,
                ProductPriceInCents = product.PriceInCents,
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                CreatedAt = now,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                Product = product,
                Customer = customer
            };

            _subscriptions.Add(created);
            return created;
        }
    }

    /// <summary>
    /// Inserts a subscription without going through the create path, standing in for a write another
    /// application instance made against the same Maxio site.
    /// </summary>
    public MaxioSubscription InsertCompetingSubscription(long customerId, string productHandle, string reference)
    {
        var product = CatalogProducts.Single(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var competing = new MaxioSubscription
            {
                Id = _nextId++,
                State = "active",
                Reference = reference,
                Currency = Site.Currency,
                ProductPriceInCents = product.PriceInCents,
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                CreatedAt = now,
                Product = product,
                Customer = _customers.Single(c => c.Id == customerId)
            };

            _subscriptions.Add(competing);
            return competing;
        }
    }

    /// <summary>Puts a subscription into a terminal state, as cancelling it in Maxio would.</summary>
    public void Cancel(long subscriptionId)
    {
        lock (_gate)
        {
            var subscription = _subscriptions.Single(s => s.Id == subscriptionId);
            subscription.State = "canceled";
            subscription.CanceledAt = DateTimeOffset.UtcNow;
        }
    }

    public int SubscriptionCount
    {
        get
        {
            lock (_gate)
            {
                return _subscriptions.Count;
            }
        }
    }

    public int CustomerCount
    {
        get
        {
            lock (_gate)
            {
                return _customers.Count;
            }
        }
    }

    private IReadOnlyList<MaxioProduct> CatalogProducts => Products ?? new[]
    {
        NewProduct(1, ProPlanHandle, "Pro Plan", 29900),
        NewProduct(2, BasicPlanHandle, "Basic Plan", 2900)
    };

    private static MaxioProduct NewProduct(long id, string handle, string name, long priceInCents) => new()
    {
        Id = id,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Id = 500, Handle = ProductFamilyHandle }
    };

    private static MaxioApiException DuplicateReference(string method, string path) => new(
        HttpStatusCode.UnprocessableEntity,
        method,
        path,
        new[] { "Reference: must be unique - that value has been taken." });

    private Task DelayAsync() => CallLatency > TimeSpan.Zero ? Task.Delay(CallLatency) : Task.CompletedTask;
}
