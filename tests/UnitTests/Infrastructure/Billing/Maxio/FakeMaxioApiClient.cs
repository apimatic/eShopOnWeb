using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// An in-memory stand-in for the Maxio API that reproduces the behaviour the billing service
/// depends on — above all, the per-site uniqueness of <c>reference</c> on customers and
/// subscriptions, which is what makes subscribing idempotent.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private long _nextId = 1000;

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public List<MaxioProduct> Products { get; } = new()
    {
        new MaxioProduct
        {
            Id = 1,
            Handle = "pro-plan",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "demo-family", Name = "Demo Family" },
        },
        new MaxioProduct
        {
            Id = 2,
            Handle = "starter-plan",
            Name = "Starter Plan",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "demo-family", Name = "Demo Family" },
        },
    };

    /// <summary>Set to make every call fail with this status, e.g. to simulate rejected credentials.</summary>
    public HttpStatusCode? FailEveryCallWith { get; set; }

    /// <summary>Handle of the only product family that exists; anything else reads as absent.</summary>
    public string? KnownProductFamilyHandle { get; set; } = "demo-family";

    /// <summary>Runs just before a create is applied, to interleave a competing write in a test.</summary>
    public Func<Task>? BeforeCreateSubscription { get; set; }

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Get, "site.json");
        return Task.FromResult(new MaxioSite { Id = 1, Subdomain = "acme", Currency = "USD", Test = true });
    }

    public Task<IReadOnlyList<MaxioProduct>?> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Get, "product_families");

        if (!string.Equals(productFamilyHandle, KnownProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<MaxioProduct>?>(null);
        }

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MaxioProduct>?>(Products.ToList());
        }
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Get, "customers/lookup.json");

        lock (_gate)
        {
            return Task.FromResult(Customers.FirstOrDefault(c => c.Reference == reference));
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Post, "customers.json");

        lock (_gate)
        {
            CreateCustomerCalls++;

            if (Customers.Any(c => c.Reference == customer.Reference))
            {
                throw DuplicateReference(HttpMethod.Post, "customers.json");
            }

            var created = new MaxioCustomer
            {
                Id = _nextId++,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Organization = customer.Organization,
                Reference = customer.Reference,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            Customers.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Get, "customers/subscriptions.json");

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                Subscriptions.Where(s => s.Customer?.Id == customerId).ToList());
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Post, "subscriptions.json");

        if (BeforeCreateSubscription is not null)
        {
            await BeforeCreateSubscription().ConfigureAwait(false);
        }

        lock (_gate)
        {
            CreateSubscriptionCalls++;

            if (Subscriptions.Any(s => s.Reference == subscription.Reference))
            {
                throw DuplicateReference(HttpMethod.Post, "subscriptions.json");
            }

            var product = Products.FirstOrDefault(p =>
                string.Equals(p.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase));

            if (product is null)
            {
                throw new MaxioApiException(
                    HttpMethod.Post,
                    "subscriptions.json",
                    HttpStatusCode.UnprocessableEntity,
                    new[] { $"Product with API Handle '{subscription.ProductHandle}' does not exist for this site." });
            }

            var now = DateTimeOffset.UtcNow;
            var created = new MaxioSubscription
            {
                Id = _nextId++,
                State = "active",
                Reference = subscription.Reference,
                ProductPriceInCents = product.PriceInCents,
                BalanceInCents = product.PriceInCents,
                Currency = "USD",
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                CreatedAt = now,
                ActivatedAt = now,
                Product = product,
                Customer = Customers.First(c => c.Id == subscription.CustomerId),
            };

            Subscriptions.Add(created);
            return created;
        }
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing(HttpMethod.Get, "subscriptions/lookup.json");

        lock (_gate)
        {
            return Task.FromResult(Subscriptions.FirstOrDefault(s => s.Reference == reference));
        }
    }

    /// <summary>Adds a subscription directly, bypassing the create path, to set up a starting state.</summary>
    public MaxioSubscription Seed(MaxioCustomer customer, string productHandle, string reference, string state)
    {
        lock (_gate)
        {
            var product = Products.First(p => p.Handle == productHandle);
            var subscription = new MaxioSubscription
            {
                Id = _nextId++,
                State = state,
                Reference = reference,
                ProductPriceInCents = product.PriceInCents,
                Currency = "USD",
                CreatedAt = DateTimeOffset.UtcNow,
                Product = product,
                Customer = customer,
            };

            Subscriptions.Add(subscription);
            return subscription;
        }
    }

    public MaxioCustomer SeedCustomer(string reference, string email)
    {
        lock (_gate)
        {
            var customer = new MaxioCustomer
            {
                Id = _nextId++,
                Reference = reference,
                Email = email,
                FirstName = "Seed",
                LastName = "Customer",
                CreatedAt = DateTimeOffset.UtcNow,
            };

            Customers.Add(customer);
            return customer;
        }
    }

    /// <summary>Mirrors Maxio's real message, which the client detects to spot a lost race.</summary>
    private static MaxioApiException DuplicateReference(HttpMethod method, string path) =>
        new(method, path, HttpStatusCode.UnprocessableEntity,
            new[] { "Reference: must be unique - that value has been taken." });

    private void ThrowIfFailing(HttpMethod method, string path)
    {
        if (FailEveryCallWith is { } status)
        {
            throw new MaxioApiException(method, path, status);
        }
    }
}
