using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// In-memory double of the Maxio Advanced Billing API that mirrors the real service's
/// observed contract: customer references are unique, but creating the same plan
/// subscription twice is allowed (the app is responsible for deduplicating).
/// </summary>
public class FakeMaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    public static readonly MaxioPlan ProPlan = new(7126957, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false);
    public static readonly MaxioPlan BasicPlan = new(7126958, "basic-plan", "Basic Plan", null, 2900, 1, "month", false);

    private readonly List<MaxioCustomer> _customers = new();
    private readonly List<MaxioSubscriptionInfo> _subscriptions = new();
    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 500;

    public int CustomersCreated { get; private set; }
    public int SubscriptionsCreated { get; private set; }
    public Func<string, MaxioCustomer?>? InitialCustomer { get; set; }

    public IReadOnlyList<MaxioPlan> Plans { get; set; } = new List<MaxioPlan> { ProPlan, BasicPlan };

    public Task<IReadOnlyList<MaxioPlan>> GetPlansForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<MaxioPlan>)Plans.Where(p => !p.Archived).ToList());

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var customer = _customers.SingleOrDefault(c => c.Reference == reference) ?? InitialCustomer?.Invoke(reference);
        return Task.FromResult(customer);
    }

    public Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string? email, CancellationToken cancellationToken = default)
    {
        if (_customers.Any(c => c.Reference == reference) || InitialCustomer?.Invoke(reference) != null)
        {
            throw new MaxioBillingProviderException("Reference: must be unique - that value has been taken.",
                422, new[] { "Reference: must be unique - that value has been taken." }, duplicateCustomerReference: true);
        }

        CustomersCreated++;
        var created = new MaxioCustomer(_nextCustomerId++, reference, email);
        _customers.Add(created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<MaxioSubscriptionInfo>)_subscriptions.Where(s => s.CustomerId == customerId).ToList());

    public Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int customerId, MaxioPlan plan, CancellationToken cancellationToken = default)
    {
        SubscriptionsCreated++;
        var subscription = new MaxioSubscriptionInfo(_nextSubscriptionId++, customerId, plan.ProductId, plan.Handle, plan.Name,
            plan.PriceInCents, "active", DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);
        _subscriptions.Add(subscription);
        return Task.FromResult(subscription);
    }

    public void CancelSubscription(int subscriptionId)
    {
        var index = _subscriptions.FindIndex(s => s.Id == subscriptionId);
        if (index >= 0)
        {
            var s = _subscriptions[index];
            _subscriptions[index] = new MaxioSubscriptionInfo(s.Id, s.CustomerId, s.ProductId, s.ProductHandle, s.ProductName,
                s.PriceInCents, "canceled", null, s.CurrentPeriodStartedAt, s.CurrentPeriodEndsAt, s.CreatedAt);
        }
    }
}
