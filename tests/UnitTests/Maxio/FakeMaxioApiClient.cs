using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Maxio.Http;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// A thread-safe, in-memory stand-in for the Maxio Advanced Billing API. It deliberately
/// provides NO duplicate protection (mirroring the real API): duplicate customers/subscriptions
/// are only avoided by the service under test.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public string Currency { get; set; } = "USD";

    public long ProductFamilyId { get; } = 1;

    public IReadOnlyList<MaxioCustomer> CreatedCustomers
    {
        get { lock (_gate) { return _customers.ToList(); } }
    }

    public IReadOnlyList<MaxioSubscription> CreatedSubscriptions
    {
        get { lock (_gate) { return _subscriptions.ToList(); } }
    }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new MaxioSite { Id = 1, Name = "Test Site", Subdomain = "test", Currency = Currency });
    }

    public Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProductFamily> families = new List<MaxioProductFamily>
        {
            new() { Id = ProductFamilyId, Name = "eShopSubscribe", Handle = "eshop-subscribe" }
        };
        return Task.FromResult(families);
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyAsync(long productFamilyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProduct> products = new List<MaxioProduct>
        {
            new() { Id = 1, Name = "Pro Plan", Handle = "eshop-pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", RequireCreditCard = false },
            new() { Id = 2, Name = "Basic Plan", Handle = "basic-plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month", RequireCreditCard = false },
            new() { Id = 3, Name = "Retired Plan", Handle = "retired-plan", PriceInCents = 1000, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
        };
        return Task.FromResult(products);
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var match = _customers.LastOrDefault(c => c.Reference == reference);
            return Task.FromResult(match);
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _customers.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<MaxioSubscription> matches = _subscriptions
                .Where(s => s.Customer?.Id == customerId)
                .ToList();
            return Task.FromResult(matches);
        }
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioNewSubscription subscription, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var customer = _customers.First(c => c.Id == subscription.CustomerId);
            var product = new MaxioProduct
            {
                Id = subscription.ProductHandle == "eshop-pro" ? 1 : 2,
                Name = subscription.ProductHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
                Handle = subscription.ProductHandle!,
                PriceInCents = subscription.ProductHandle == "eshop-pro" ? 29900 : 2900,
                Interval = 1,
                IntervalUnit = "month"
            };

            var created = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                Product = product,
                Customer = customer,
                ProductPriceInCents = product.PriceInCents,
                Currency = Currency,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                BalanceInCents = product.PriceInCents,
                CreatedAt = DateTimeOffset.UtcNow,
                ActivatedAt = DateTimeOffset.UtcNow
            };
            _subscriptions.Add(created);
            return Task.FromResult(created);
        }
    }

    public void SeedSubscription(MaxioSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }
    }
}
