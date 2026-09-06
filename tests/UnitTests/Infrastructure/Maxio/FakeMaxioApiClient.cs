using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// In-memory stand-in for the Maxio API that enforces the two invariants the integration's
/// idempotency depends on: customer references and subscription references are unique, and a
/// violation is reported as a 422 saying the value "must be unique".
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _sync = new();
    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscription> _subscriptions = new();

    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 5000;

    public List<MaxioProduct> Products { get; } = new();

    public int CreateCustomerCalls { get; private set; }

    public int CreateSubscriptionCalls { get; private set; }

    public IReadOnlyList<MaxioSubscription> Subscriptions
    {
        get { lock (_sync) { return _subscriptions.ToList(); } }
    }

    /// <summary>Runs before each write, to inject races or latency into a test.</summary>
    public Func<Task>? BeforeWrite { get; set; }

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());

    public Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_customers.FirstOrDefault(c =>
                string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        if (BeforeWrite is not null) await BeforeWrite();

        lock (_sync)
        {
            CreateCustomerCalls++;

            if (_customers.Any(c => string.Equals(c.Reference, request.Customer.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw Duplicate();
            }

            var customer = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = request.Customer.FirstName,
                LastName = request.Customer.LastName,
                Email = request.Customer.Email,
                Reference = request.Customer.Reference,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _customers.Add(customer);

            return customer;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                _subscriptions.Where(s => s.Customer?.Id == customerId).ToList());
        }
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_subscriptions.FirstOrDefault(s =>
                string.Equals(s.Reference, reference, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (BeforeWrite is not null) await BeforeWrite();

        lock (_sync)
        {
            CreateSubscriptionCalls++;

            if (_subscriptions.Any(s => string.Equals(s.Reference, request.Subscription.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw Duplicate();
            }

            var product = Products.FirstOrDefault(p =>
                string.Equals(p.Handle, request.Subscription.ProductHandle, StringComparison.OrdinalIgnoreCase));

            var subscription = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                Reference = request.Subscription.Reference,
                Product = product,
                ProductPriceInCents = product?.PriceInCents ?? 0,
                Currency = "USD",
                PaymentCollectionMethod = request.Subscription.PaymentCollectionMethod,
                Customer = _customers.FirstOrDefault(c => c.Id == request.Subscription.CustomerId),
                CreatedAt = DateTimeOffset.UtcNow,
                CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1)
            };

            _subscriptions.Add(subscription);

            return subscription;
        }
    }

    /// <summary>Marks a subscription terminal, as Maxio would after a cancellation.</summary>
    public void Cancel(int subscriptionId)
    {
        lock (_sync)
        {
            var subscription = _subscriptions.Single(s => s.Id == subscriptionId);
            subscription.State = "canceled";
            subscription.CanceledAt = DateTimeOffset.UtcNow;
        }
    }

    public void AddProduct(string handle, string name, long priceInCents, bool requireCreditCard = false) =>
        Products.Add(new MaxioProduct
        {
            Id = Products.Count + 1,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = requireCreditCard,
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        });

    private static MaxioApiException Duplicate() =>
        new("Maxio rejected the request", 422, new[] { "Reference: must be unique - that value has been taken." });
}
