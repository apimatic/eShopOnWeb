using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// In-memory stand-in for the Maxio Advanced Billing API used by the integration tests.
/// It reproduces the real API's observable behaviour that the subscription service relies on:
/// customers are unique by reference, duplicate subscriptions are allowed (the service itself
/// is responsible for idempotency), and list/create endpoints return the documented envelopes.
/// </summary>
public sealed class FakeMaxioClient : IMaxioClient
{
    private readonly object _gate = new();
    private long _nextCustomerId = 1;
    private long _nextSubscriptionId = 1;

    public FakeMaxioClient()
    {
        Products = new List<MaxioProduct>
        {
            new()
            {
                Id = 1,
                Name = "Pro Plan",
                Handle = "eshop-pro",
                Description = "Pro subscription",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month"
            },
            new()
            {
                Id = 2,
                Name = "Basic Plan",
                Handle = "basic-plan",
                Description = "Basic subscription",
                PriceInCents = 2900,
                Interval = 1,
                IntervalUnit = "month"
            }
        };
    }

    public List<MaxioProduct> Products { get; }

    public List<MaxioCustomer> Customers { get; } = new();

    public List<MaxioSubscription> Subscriptions { get; } = new();

    public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var available = Products.Where(p => p.ArchivedAt is null).ToList();
            return Task.FromResult<IReadOnlyList<MaxioProduct>>(available);
        }
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Customers.FirstOrDefault(c =>
                string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Customers.Any(c => string.Equals(c.Reference, customer.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw new MaxioApiException(422, "POST customers.json",
                    new[] { "Reference: must be unique - that value has been taken." });
            }

            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Organization = customer.Organization,
                Reference = customer.Reference,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Customers.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var subscriptions = Subscriptions
                .Where(s => s.Customer is not null && s.Customer.Id == customerId)
                .ToList();
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(subscriptions);
        }
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var attributes = request.Subscription;
            var customer = Customers.FirstOrDefault(c =>
                string.Equals(c.Reference, attributes.CustomerReference, StringComparison.OrdinalIgnoreCase));
            if (customer is null)
            {
                throw new MaxioApiException(422, "POST subscriptions.json",
                    new[] { "Customer reference does not match an existing customer." });
            }

            var product = Products.FirstOrDefault(p =>
                string.Equals(p.Handle, attributes.ProductHandle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                throw new MaxioApiException(422, "POST subscriptions.json",
                    new[] { $"Product handle '{attributes.ProductHandle}' was not found." });
            }

            var now = DateTimeOffset.UtcNow;
            var created = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                Currency = "USD",
                BalanceInCents = 0,
                ProductPriceInCents = product.PriceInCents,
                PaymentCollectionMethod = attributes.PaymentCollectionMethod,
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(product.Interval),
                NextAssessmentAt = now.AddMonths(product.Interval),
                ActivatedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Customer = customer,
                Product = product
            };
            Subscriptions.Add(created);
            return Task.FromResult(created);
        }
    }

    public int CountSubscriptions(string customerReference, string productHandle)
    {
        lock (_gate)
        {
            return Subscriptions.Count(s =>
                string.Equals(s.Customer?.Reference, customerReference, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        }
    }

    public int CountCustomers(string customerReference)
    {
        lock (_gate)
        {
            return Customers.Count(c =>
                string.Equals(c.Reference, customerReference, StringComparison.OrdinalIgnoreCase));
        }
    }
}
