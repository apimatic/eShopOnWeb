using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stands in for the real Maxio-backed service so these tests exercise routing, JWT auth and
/// JSON contracts without depending on network access or real Maxio credentials. The
/// idempotency/business logic of the real service is covered separately by
/// UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests.
/// </summary>
public class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    public static readonly SubscriptionPlan ProPlan = new() { Handle = "eshop-pro", Name = "Pro Plan", Price = 299m, IntervalCount = 1, IntervalUnit = "month" };

    private readonly Dictionary<string, List<CustomerSubscription>> _subscriptionsByCustomerReference = new();

    public string? LastCustomerReferenceSeen { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new List<SubscriptionPlan> { ProPlan });

    public Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default)
    {
        LastCustomerReferenceSeen = customerReference;

        if (planHandle != ProPlan.Handle)
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        if (!_subscriptionsByCustomerReference.TryGetValue(customerReference, out var subscriptions))
        {
            subscriptions = new List<CustomerSubscription>();
            _subscriptionsByCustomerReference[customerReference] = subscriptions;
        }

        var existing = subscriptions.FirstOrDefault(s => s.Plan.Handle == planHandle);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        var created = new CustomerSubscription { SubscriptionId = 1000 + subscriptions.Count, State = "active", Plan = ProPlan };
        subscriptions.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        LastCustomerReferenceSeen = customerReference;
        IReadOnlyList<CustomerSubscription> result = _subscriptionsByCustomerReference.TryGetValue(customerReference, out var subscriptions)
            ? subscriptions
            : new List<CustomerSubscription>();
        return Task.FromResult(result);
    }
}
