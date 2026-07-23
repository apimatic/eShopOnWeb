using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// A hand-written <see cref="IBillingClient"/> that records what the service asked of it. The service's
/// job is to decide what must never reach the provider, so the tests need to assert on calls that did
/// <em>not</em> happen as much as on calls that did.
/// </summary>
public sealed class FakeBillingClient : IBillingClient
{
    public List<string> Calls { get; } = new();

    public List<BillingPlan> Plans { get; } = new();

    public MeteredComponent? Component { get; set; }

    public BillingCustomer? ExistingCustomer { get; set; }

    public List<Subscription> CustomerSubscriptions { get; } = new();

    public Subscription? SubscriptionById { get; set; }

    public Subscription? CreatedSubscription { get; set; }

    public Subscription? UpdatedSubscription { get; set; }

    public UsageRecord RecordedUsage { get; set; } = new(1, 1, null, DateTimeOffset.UnixEpoch);

    public int? PeriodToDate { get; set; }

    public PlanChangePreview? Preview { get; set; }

    /// <summary>Set to make the period-to-date read-back fail the way a flaky provider would.</summary>
    public Exception? PeriodToDateFailure { get; set; }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListPlansAsync));
        return Task.FromResult<IReadOnlyList<BillingPlan>>(Plans);
    }

    public Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindPlanByHandleAsync)}:{planHandle}");
        return Task.FromResult(Plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindComponentByHandleAsync)}:{componentHandle}");
        return Task.FromResult(Component);
    }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(FindCustomerByReferenceAsync)}:{customerReference}");
        return Task.FromResult(ExistingCustomer);
    }

    public Task<BillingCustomer> CreateCustomerAsync(BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateCustomerAsync)}:{registration.Reference}");
        LastRegistration = registration;

        var created = new BillingCustomer(MaxioPayloads.CustomerId, registration.Reference, registration.Email,
            registration.FirstName, registration.LastName);

        ExistingCustomer = created;
        return Task.FromResult(created);
    }

    public BillingCustomerRegistration? LastRegistration { get; private set; }

    public Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(CreateSubscriptionAsync)}:{planHandle}");
        return Task.FromResult(CreatedSubscription
            ?? throw new BillingProviderException("The test did not configure a created subscription."));
    }

    public Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ListSubscriptionsForCustomerAsync)}:{customerId}");
        return Task.FromResult<IReadOnlyList<Subscription>>(CustomerSubscriptions);
    }

    public Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetSubscriptionAsync)}:{subscriptionId}");
        return Task.FromResult(SubscriptionById);
    }

    public Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, int quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(RecordUsageAsync)}:{subscriptionId}:{quantity}");
        return Task.FromResult(RecordedUsage);
    }

    public Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(GetPeriodToDateUsageAsync)}:{subscriptionId}");

        if (PeriodToDateFailure is not null)
        {
            return Task.FromException<int?>(PeriodToDateFailure);
        }

        return Task.FromResult(PeriodToDate);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(PreviewPlanChangeAsync)}:{targetPlanHandle}:{timing}");
        return Task.FromResult(Preview
            ?? throw new BillingProviderException("The test did not configure a preview."));
    }

    public Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ChangePlanAsync)}:{targetPlanHandle}:{timing}");
        return Task.FromResult(UpdatedSubscription
            ?? throw new BillingProviderException("The test did not configure an updated subscription."));
    }

    public Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action,
        string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{nameof(ApplyLifecycleActionAsync)}:{action}");
        return Task.FromResult(UpdatedSubscription
            ?? throw new BillingProviderException("The test did not configure an updated subscription."));
    }

    /// <summary>True when no call whose name starts with <paramref name="prefix"/> was made.</summary>
    public bool NeverCalled(string prefix) => !Calls.Any(call => call.StartsWith(prefix, StringComparison.Ordinal));
}
