using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// In-memory stand-in for the Maxio API that keeps the server side rules the integration relies on:
/// customer and subscription references are unique, and a replayed uniqueness token is refused.
/// Testing against those rules is the point - they are what makes the integration idempotent.
/// </summary>
public class FakeMaxioApi : IMaxioApiClient
{
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();
    private readonly HashSet<string> _uniquenessTokens = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    private long _nextCustomerId = 1000;
    private long _nextSubscriptionId = 5000;

    public List<MaxioProduct> Products { get; } = new();

    public MaxioSite Site { get; set; } = new() { Id = 1, Subdomain = "test", Currency = "USD", RelationshipInvoicingEnabled = true, Test = true };

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }
    public int ReadSiteCalls { get; private set; }

    /// <summary>Widens the window between a read and the write that follows it, to expose races.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Simulates a create whose response never made it back: the subscription is stored, but the
    /// caller is told the submission was a duplicate. This is what a transport level retry sees.
    /// </summary>
    public bool LoseNextCreateResponse { get; set; }

    /// <summary>Simulates a duplicate submission for a record that no longer exists, e.g. a purged one.</summary>
    public bool RejectNextCreateAsDuplicate { get; set; }

    public static MaxioProduct Product(string handle, string name, long priceInCents = 1000, bool requireCreditCard = false, DateTimeOffset? archivedAt = null) =>
        new()
        {
            Id = Math.Abs(handle.GetHashCode()),
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = requireCreditCard,
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe", Name = "eShopSubscribe" }
        };

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        return Products.ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_sync)
        {
            return _customers.FirstOrDefault(customer => customer.Reference == reference);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_sync)
        {
            CreateCustomerCalls++;

            if (_customers.Any(existing => existing.Reference == customer.Reference))
            {
                throw new MaxioValidationException("POST", "customers.json",
                    new[] { "Reference: must be unique - that value has been taken." });
            }

            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                Reference = customer.Reference,
                Email = customer.Email,
                FirstName = customer.FirstName,
                LastName = customer.LastName
            };

            _customers.Add(created);
            return created;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_sync)
        {
            CreateSubscriptionCalls++;

            if (request.UniquenessToken is not null && !_uniquenessTokens.Add(request.UniquenessToken))
            {
                throw new MaxioDuplicateSubmissionException("POST", "subscriptions.json",
                    new[] { "DuplicatePrevention::DuplicateSubmissionError" });
            }

            if (_subscriptions.Any(existing => existing.Reference == request.Subscription.Reference))
            {
                throw new MaxioValidationException("POST", "subscriptions.json",
                    new[] { "Reference: must be unique - that value has been taken." });
            }

            var product = Products.FirstOrDefault(candidate => candidate.Handle == request.Subscription.ProductHandle)
                ?? throw new MaxioValidationException("POST", "subscriptions.json",
                    new[] { $"Product with API Handle '{request.Subscription.ProductHandle}' does not exist for this site." });

            var customer = _customers.First(candidate => candidate.Id == request.Subscription.CustomerId);
            var now = DateTimeOffset.UtcNow;

            var created = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                Reference = request.Subscription.Reference,
                ProductPriceInCents = product.PriceInCents,
                Currency = "USD",
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                ActivatedAt = now,
                CreatedAt = now,
                PaymentCollectionMethod = request.Subscription.PaymentCollectionMethod,
                Product = product,
                Customer = customer
            };

            if (RejectNextCreateAsDuplicate)
            {
                RejectNextCreateAsDuplicate = false;
                throw new MaxioDuplicateSubmissionException("POST", "subscriptions.json",
                    new[] { "DuplicatePrevention::DuplicateSubmissionError" });
            }

            _subscriptions.Add(created);

            if (LoseNextCreateResponse)
            {
                LoseNextCreateResponse = false;
                throw new MaxioDuplicateSubmissionException("POST", "subscriptions.json",
                    new[] { "DuplicatePrevention::DuplicateSubmissionError" });
            }

            return created;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_sync)
        {
            return _subscriptions.Where(subscription => subscription.Customer?.Id == customerId).ToList();
        }
    }

    public async Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default)
    {
        await DelayAsync();
        lock (_sync)
        {
            ReadSiteCalls++;
            return Site;
        }
    }

    /// <summary>Puts a subscription into the store without going through the create path.</summary>
    public MaxioSubscription Seed(MaxioCustomer customer, MaxioProduct product, string state, string reference)
    {
        lock (_sync)
        {
            var subscription = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = state,
                Reference = reference,
                ProductPriceInCents = product.PriceInCents,
                Currency = "USD",
                CreatedAt = DateTimeOffset.UtcNow,
                Product = product,
                Customer = customer
            };

            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public MaxioCustomer SeedCustomer(string reference, string email)
    {
        lock (_sync)
        {
            var customer = new MaxioCustomer { Id = _nextCustomerId++, Reference = reference, Email = email };
            _customers.Add(customer);
            return customer;
        }
    }

    private Task DelayAsync() => Latency > TimeSpan.Zero ? Task.Delay(Latency) : Task.CompletedTask;
}
