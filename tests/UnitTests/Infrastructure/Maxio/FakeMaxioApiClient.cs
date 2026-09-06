using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// An in-memory stand-in for a Maxio site that enforces the two invariants the billing service
/// relies on: one customer per reference, and rejection of a repeated write carrying a uniqueness
/// token it has already seen.
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private readonly HashSet<string> _seenTokens = new(StringComparer.Ordinal);

    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public List<MaxioProduct> Products { get; } = new();

    public MaxioSite? Site { get; set; } = new() { Currency = "USD", RelationshipInvoicingEnabled = true, Test = true };

    /// <summary>Widens the window in which two concurrent subscribes could both decide to create.</summary>
    public TimeSpan WriteDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Forces every create to be rejected as a duplicate, as a second app instance would see.</summary>
    public bool AlwaysRejectSubscriptionAsDuplicate { get; set; }

    /// <summary>
    /// Number of customer lookups to answer as "not found" even when the customer exists - simulates
    /// another writer creating it in the gap between our lookup and our create.
    /// </summary>
    public int HideCustomerForReads { get; set; }

    /// <summary>
    /// Number of subscription list reads to answer as empty before revealing
    /// <see cref="SubscriptionAppearingAfterReads"/> - simulates a winner still committing.
    /// </summary>
    public int HideSubscriptionForReads { get; set; }

    public MaxioSubscription? SubscriptionAppearingAfterReads { get; set; }

    public int CustomerCreateCount { get; private set; }

    public int SubscriptionCreateCount { get; private set; }

    public int SubscriptionListCount { get; private set; }

    public MaxioSubscriptionAttributes? LastSubscriptionAttributes { get; private set; }

    public IReadOnlyList<MaxioSubscription> Subscriptions
    {
        get { lock (_gate) { return _subscriptions.ToList(); } }
    }

    public IReadOnlyList<MaxioCustomer> Customers
    {
        get { lock (_gate) { return _customers.ToList(); } }
    }

    public MaxioCustomer SeedCustomer(string reference)
    {
        lock (_gate)
        {
            var customer = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                Reference = reference,
                Email = "seeded@example.com",
                CreatedAt = DateTimeOffset.UtcNow
            };
            _customers.Add(customer);
            return customer;
        }
    }

    public MaxioSubscription SeedSubscription(long customerId, string productHandle, string state)
    {
        lock (_gate)
        {
            var subscription = BuildSubscription(customerId, productHandle, state);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());

    public Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Site);

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (HideCustomerForReads > 0)
            {
                HideCustomerForReads--;
                return Task.FromResult<MaxioCustomer?>(null);
            }

            return Task.FromResult(_customers.FirstOrDefault(c =>
                string.Equals(c.Reference, reference, StringComparison.Ordinal)));
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        await DelayAsync(cancellationToken);

        lock (_gate)
        {
            if (!_seenTokens.Add("customer:" + uniquenessToken))
            {
                throw Duplicate("create customer");
            }

            if (_customers.Any(c => string.Equals(c.Reference, customer.Reference, StringComparison.Ordinal)))
            {
                throw new MaxioApiException(
                    HttpStatusCode.UnprocessableEntity,
                    new[] { "reference: must be unique." },
                    "create customer");
            }

            CustomerCreateCount++;

            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _customers.Add(created);
            return created;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            SubscriptionListCount++;

            if (HideSubscriptionForReads > 0)
            {
                HideSubscriptionForReads--;
                return Task.FromResult<IReadOnlyList<MaxioSubscription>>(Array.Empty<MaxioSubscription>());
            }

            var results = _subscriptions.Where(s => s.Customer?.Id == customerId).ToList();

            if (SubscriptionAppearingAfterReads is not null)
            {
                results.Add(SubscriptionAppearingAfterReads);
            }

            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(results);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionAttributes subscription, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        await DelayAsync(cancellationToken);

        lock (_gate)
        {
            LastSubscriptionAttributes = subscription;

            if (AlwaysRejectSubscriptionAsDuplicate || !_seenTokens.Add("subscription:" + uniquenessToken))
            {
                throw Duplicate("create subscription");
            }

            SubscriptionCreateCount++;

            var created = BuildSubscription(
                subscription.CustomerId ?? 0, subscription.ProductHandle ?? string.Empty, "active");
            created.PaymentCollectionMethod = subscription.PaymentCollectionMethod;

            _subscriptions.Add(created);
            return created;
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (WriteDelay > TimeSpan.Zero)
        {
            await Task.Delay(WriteDelay, cancellationToken);
        }
    }

    private MaxioSubscription BuildSubscription(long customerId, string productHandle, string state)
    {
        var product = Products.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        return new MaxioSubscription
        {
            Id = _nextSubscriptionId++,
            State = state,
            ProductPriceInCents = product?.PriceInCents ?? 0,
            CreatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            Customer = _customers.FirstOrDefault(c => c.Id == customerId) ?? new MaxioCustomer { Id = customerId },
            Product = product ?? new MaxioProduct { Handle = productHandle }
        };
    }

    private static MaxioApiException Duplicate(string description) =>
        new(HttpStatusCode.Conflict, new[] { "DuplicatePrevention::DuplicateSubmissionError" }, description);
}
