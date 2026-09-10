using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// In-memory, thread-safe stand-in for <see cref="IMaxioApiClient"/> used to exercise the
/// orchestration and idempotency logic of <c>MaxioSubscriptionService</c> without hitting the
/// network. Tracks how many customers/subscriptions were actually created.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private readonly List<MaxioProduct> _products;
    private readonly Dictionary<string, MaxioCustomer> _customersByReference = new(StringComparer.Ordinal);
    private readonly List<(int CustomerId, MaxioSubscription Subscription)> _subscriptions = new();

    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 5000;

    public FakeMaxioApiClient(IEnumerable<MaxioProduct> products)
    {
        _products = products.ToList();
    }

    public int CustomerCreateCount { get; private set; }
    public int SubscriptionCreateCount { get; private set; }

    /// <summary>Optional artificial latency, used to widen the race window in concurrency tests.</summary>
    public TimeSpan ArtificialLatency { get; set; } = TimeSpan.Zero;

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        lock (_gate)
        {
            return _products.ToList();
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        lock (_gate)
        {
            return _customersByReference.TryGetValue(reference, out var customer) ? customer : null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerBody customer, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        lock (_gate)
        {
            var created = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            };
            _customersByReference[customer.Reference!] = created;
            CustomerCreateCount++;
            return created;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        lock (_gate)
        {
            return _subscriptions.Where(s => s.CustomerId == customerId).Select(s => s.Subscription).ToList();
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionBody subscription, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        lock (_gate)
        {
            var product = _products.FirstOrDefault(p =>
                string.Equals(p.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase));

            var created = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                ProductPriceInCents = product?.PriceInCents ?? 0,
                Product = product,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow
            };
            _subscriptions.Add((subscription.CustomerId, created));
            SubscriptionCreateCount++;
            return created;
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (ArtificialLatency > TimeSpan.Zero)
        {
            await Task.Delay(ArtificialLatency, cancellationToken);
        }
    }
}
