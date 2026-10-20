using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

/// <summary>In-memory stand-in for the billing provider; keeps what "Maxio" would hold.</summary>
public class FakeBillingGateway : IBillingGateway
{
    private readonly object _lock = new();
    private int _nextId = 100;

    public Dictionary<string, BillingCustomer> Customers { get; } = new();
    public Dictionary<string, SubscriptionInfo> Subscriptions { get; } = new();

    public List<PlanInfo> Plans { get; } = new()
    {
        new PlanInfo("eshop-pro", "Pro", null, 29900, 1, "month"),
        new PlanInfo("basic-plan", "Basic", null, 2900, 1, "month"),
    };
    public bool PlansTruncated { get; set; }

    public int CreateCustomerCalls;
    public int CreateSubscriptionCalls;

    /// <summary>Awaited inside CreateSubscription; lets a test hold a request in flight.</summary>
    public Func<Task>? CreateSubscriptionGate { get; set; }
    /// <summary>When set, thrown by CreateSubscription. If <see cref="CreateSubscriptionLandsAnyway"/>, the write is stored first.</summary>
    public Exception? CreateSubscriptionFailure { get; set; }
    public bool CreateSubscriptionLandsAnyway { get; set; }

    public Task<PlanListResult> ListPlansAsync(CancellationToken ct) =>
        Task.FromResult(new PlanListResult(Plans.ToList(), PlansTruncated));

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(Customers.TryGetValue(reference, out var c) ? c : null);
        }
    }

    public Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken ct)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref CreateCustomerCalls);
            var created = new BillingCustomer(_nextId++);
            Customers[customer.Reference] = created;
            return Task.FromResult(created);
        }
    }

    public Task<SubscriptionInfo?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(Subscriptions.TryGetValue(reference, out var s) ? s : null);
        }
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        Interlocked.Increment(ref CreateSubscriptionCalls);
        if (CreateSubscriptionGate is not null) await CreateSubscriptionGate();

        SubscriptionInfo info;
        lock (_lock)
        {
            var plan = Plans.First(p => p.Handle == planHandle);
            info = new SubscriptionInfo(_nextId++, plan.Handle, plan.Name, plan.PriceInCents, "USD", "active",
                DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
            if (CreateSubscriptionFailure is null || CreateSubscriptionLandsAnyway)
            {
                Subscriptions[reference] = info;
            }
        }
        if (CreateSubscriptionFailure is not null) throw CreateSubscriptionFailure;
        return info;
    }

    public Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SubscriptionInfo>>(Subscriptions.Values.ToList());
}
