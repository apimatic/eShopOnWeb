using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// An in-memory Maxio, faithful to the parts of the provider's behaviour the integration relies
/// on: customer references and subscription references are unique, lookups answer "no such
/// record" rather than failing, and a subscription is created against a product handle.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _sync = new();
    private int _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new();
    public List<MaxioCustomer> Customers { get; } = new();
    public List<MaxioSubscription> Subscriptions { get; } = new();

    public List<string> RequestedProductFamilies { get; } = new();
    public List<MaxioCreateCustomer> CreatedCustomers { get; } = new();
    public List<MaxioCreateSubscription> CreatedSubscriptions { get; } = new();

    /// <summary>Slows down creates so concurrent callers genuinely overlap.</summary>
    public TimeSpan CreateDelay { get; set; }

    public MaxioApiException? ListProductsFailure { get; set; }
    public MaxioApiException? CreateCustomerFailure { get; set; }
    public MaxioApiException? CreateSubscriptionFailure { get; set; }

    /// <summary>The customer a re-read finds after <see cref="CreateCustomerFailure"/> is thrown.</summary>
    public MaxioCustomer? CustomerAppearsAfterFailedCreate { get; set; }

    /// <summary>The subscription <c>findSubscription</c> resolves any reference to.</summary>
    public MaxioSubscription? SubscriptionForAnyReference { get; set; }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, CancellationToken cancellationToken = default)
    {
        if (ListProductsFailure is not null)
        {
            throw ListProductsFailure;
        }

        lock (_sync)
        {
            RequestedProductFamilies.Add(productFamilyIdOrHandle);
        }

        IReadOnlyList<MaxioProduct> page1 = page == 1 ? Products.ToList() : Array.Empty<MaxioProduct>();
        return Task.FromResult(page1);
    }

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (CustomerAppearsAfterFailedCreate is not null &&
                CreatedCustomers.Count > 0 &&
                !Customers.Contains(CustomerAppearsAfterFailedCreate))
            {
                Customers.Add(CustomerAppearsAfterFailedCreate);
            }

            return Task.FromResult(Customers.FirstOrDefault(customer =>
                string.Equals(customer.Reference, reference, StringComparison.Ordinal)));
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            CreatedCustomers.Add(customer);
        }

        if (CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        if (CreateCustomerFailure is not null)
        {
            throw CreateCustomerFailure;
        }

        lock (_sync)
        {
            if (Customers.Any(existing => string.Equals(existing.Reference, customer.Reference, StringComparison.Ordinal)))
            {
                throw new MaxioApiException(
                    "reference taken", System.Net.HttpStatusCode.UnprocessableEntity, new[] { "Reference: must be unique." });
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

            Customers.Add(created);
            return created;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<MaxioSubscription> owned = Subscriptions
                .Where(subscription => subscription.Customer?.Id == customerId)
                .ToList();

            return Task.FromResult(owned);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            CreatedSubscriptions.Add(subscription);
        }

        if (CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        if (CreateSubscriptionFailure is not null)
        {
            throw CreateSubscriptionFailure;
        }

        lock (_sync)
        {
            if (subscription.Reference is not null &&
                Subscriptions.Any(existing => string.Equals(existing.Reference, subscription.Reference, StringComparison.Ordinal)))
            {
                throw new MaxioApiException(
                    "reference taken", System.Net.HttpStatusCode.UnprocessableEntity, new[] { "Reference: must be unique." });
            }

            var product = Products.FirstOrDefault(candidate =>
                string.Equals(candidate.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new MaxioApiException(
                    "unknown product",
                    System.Net.HttpStatusCode.UnprocessableEntity,
                    new[] { $"Product with API Handle '{subscription.ProductHandle}' does not exist for this site." });

            var now = DateTimeOffset.UtcNow;
            var created = new MaxioSubscription
            {
                Id = _nextId++,
                State = "active",
                Product = product,
                ProductPriceInCents = product.PriceInCents,
                Currency = "USD",
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                CreatedAt = now,
                ActivatedAt = now,
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                Customer = Customers.FirstOrDefault(customer => customer.Id == subscription.CustomerId)
            };

            Subscriptions.Add(created);
            return created;
        }
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (SubscriptionForAnyReference is not null)
            {
                return Task.FromResult<MaxioSubscription?>(SubscriptionForAnyReference);
            }

            return Task.FromResult(Subscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, reference, StringComparison.Ordinal)));
        }
    }
}
