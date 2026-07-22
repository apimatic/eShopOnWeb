using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// A hand-rolled fake of the provider-agnostic <see cref="IBillingClient"/> seam, used to test the
/// orchestration in <c>SubscriptionService</c> in isolation from HTTP. Behaviour is controlled with
/// public delegates; calls are recorded so tests can assert what the service did (and did not) do.
/// </summary>
public sealed class FakeBillingClient : IBillingClient
{
    public List<string> Calls { get; } = new();

    public Func<string, BillingCustomer?> OnFindCustomer { get; set; } = _ => null;
    public Func<string, string, BillingCustomer> OnCreateCustomer { get; set; } =
        (reference, email) => new BillingCustomer(1, reference, email);
    public Func<int, IReadOnlyCollection<CustomerSubscription>> OnListCustomerSubscriptions { get; set; } =
        _ => Array.Empty<CustomerSubscription>();
    public Func<int, string, CustomerSubscription> OnCreateSubscription { get; set; } =
        (customerId, handle) => Fake.Subscription(500, "active", handle, customerId: customerId);
    public Func<int, CustomerSubscription> OnGetSubscription { get; set; } =
        id => Fake.Subscription(id, "active", "eshop-pro");
    public Func<MeteredComponentInfo> OnGetMeteredComponent { get; set; } =
        () => new MeteredComponentInfo(3057195, "api-call", "metered_component", 0.01m);
    public Func<int, int, string?, int> OnRecordUsage { get; set; } = (_, quantity, _) => quantity;
    public Func<int, decimal?> OnGetUsageBalance { get; set; } = _ => 0m;
    public Func<int, string, bool, PlanChangePreview> OnPreviewPlanChange { get; set; } =
        (_, handle, immediate) => new PlanChangePreview(handle, immediate, 0m, 50m, 50m, 0m);
    public Func<int, string, bool, CustomerSubscription> OnChangePlan { get; set; } =
        (id, handle, _) => Fake.Subscription(id, "active", handle);
    public Func<int, CustomerSubscription> OnPause { get; set; } = id => Fake.Subscription(id, "on_hold", "eshop-pro");
    public Func<int, CustomerSubscription> OnResume { get; set; } = id => Fake.Subscription(id, "active", "eshop-pro");
    public Func<int, bool, string?, CustomerSubscription> OnCancel { get; set; } =
        (id, _, _) => Fake.Subscription(id, "canceled", "eshop-pro");
    public Func<int, CustomerSubscription> OnReactivate { get; set; } = id => Fake.Subscription(id, "active", "eshop-pro");

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("ListPlans");
        return Task.FromResult<IReadOnlyCollection<SubscriptionPlan>>(new List<SubscriptionPlan>());
    }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        Calls.Add("FindCustomer");
        return Task.FromResult(OnFindCustomer(reference));
    }

    public Task<BillingCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken = default)
    {
        Calls.Add("CreateCustomer");
        return Task.FromResult(OnCreateCustomer(reference, email));
    }

    public Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add("CreateSubscription");
        return Task.FromResult(OnCreateSubscription(customerId, productHandle));
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add("ListCustomerSubscriptions");
        return Task.FromResult(OnListCustomerSubscriptions(customerId));
    }

    public Task<CustomerSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add("GetSubscription");
        return Task.FromResult(OnGetSubscription(subscriptionId));
    }

    public Task<MeteredComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("GetMeteredComponent");
        return Task.FromResult(OnGetMeteredComponent());
    }

    public Task<int> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Calls.Add("RecordUsage");
        return Task.FromResult(OnRecordUsage(subscriptionId, quantity, memo));
    }

    public Task<decimal?> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add("GetUsageBalance");
        return Task.FromResult(OnGetUsageBalance(subscriptionId));
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        Calls.Add("PreviewPlanChange");
        return Task.FromResult(OnPreviewPlanChange(subscriptionId, targetProductHandle, applyImmediately));
    }

    public Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        Calls.Add("ChangePlan");
        return Task.FromResult(OnChangePlan(subscriptionId, targetProductHandle, applyImmediately));
    }

    public Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add("Pause");
        return Task.FromResult(OnPause(subscriptionId));
    }

    public Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add("Resume");
        return Task.FromResult(OnResume(subscriptionId));
    }

    public Task<CustomerSubscription> CancelAsync(int subscriptionId, bool immediate, string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add("Cancel");
        return Task.FromResult(OnCancel(subscriptionId, immediate, reason));
    }

    public Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add("Reactivate");
        return Task.FromResult(OnReactivate(subscriptionId));
    }
}

public static class Fake
{
    public static CustomerSubscription Subscription(int id, string state, string productHandle,
        int customerId = 1, decimal price = 299.00m) =>
        new(id, state, customerId, "demouser@microsoft.com", productHandle, "Pro Plan", price, "month",
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"), false, null);
}

/// <summary>Records every MediatR notification the service publishes (or optionally throws).</summary>
public sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = new();
    public bool ThrowOnPublish { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (notification is INotification n)
        {
            return Publish(n, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (ThrowOnPublish)
        {
            throw new InvalidOperationException("Simulated handler failure");
        }

        Published.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
}
