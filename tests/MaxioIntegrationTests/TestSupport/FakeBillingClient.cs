using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;

/// <summary>
/// Hand-written test double for the provider-agnostic seam, used to exercise
/// <c>SubscriptionService</c>'s orchestration logic (idempotency, illegal-transition guards,
/// stale-preview rejection) in isolation from the wire-level <c>MaxioBillingClient</c> tests.
/// Each member is backed by a settable delegate so a test only wires up what it needs; anything
/// left unset throws, so an unexpectedly-invoked call fails the test loudly.
/// </summary>
public class FakeBillingClient : IBillingClient
{
    public List<string> Calls { get; } = new();

    public Func<CancellationToken, Task<IReadOnlyList<BillingPlan>>>? OnListPlans { get; set; }
    public Func<CancellationToken, Task<BillingComponentInfo>>? OnGetMeteredComponent { get; set; }
    public Func<string, string, string, string, CancellationToken, Task>? OnEnsureCustomer { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<Subscription>>>? OnListCustomerSubscriptions { get; set; }
    public Func<string, string, CancellationToken, Task<Subscription>>? OnCreateSubscription { get; set; }
    public Func<int, CancellationToken, Task<Subscription>>? OnGetSubscription { get; set; }
    public Func<int, decimal, string?, CancellationToken, Task<UsageRecordResult>>? OnRecordUsage { get; set; }
    public Func<int, string, bool, CancellationToken, Task<PlanChangePreview>>? OnPreviewPlanChange { get; set; }
    public Func<int, string, bool, CancellationToken, Task<Subscription>>? OnCommitPlanChange { get; set; }
    public Func<int, CancellationToken, Task<Subscription>>? OnPause { get; set; }
    public Func<int, CancellationToken, Task<Subscription>>? OnResume { get; set; }
    public Func<int, bool, string?, CancellationToken, Task<Subscription>>? OnCancel { get; set; }
    public Func<int, CancellationToken, Task<Subscription>>? OnReactivate { get; set; }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListPlansAsync));
        return (OnListPlans ?? throw NotConfigured(nameof(ListPlansAsync)))(cancellationToken);
    }

    public Task<BillingComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetMeteredComponentAsync));
        return (OnGetMeteredComponent ?? throw NotConfigured(nameof(GetMeteredComponentAsync)))(cancellationToken);
    }

    public Task EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(EnsureCustomerAsync));
        return (OnEnsureCustomer ?? throw NotConfigured(nameof(EnsureCustomerAsync)))(customerReference, email, firstName, lastName, cancellationToken);
    }

    public Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ListCustomerSubscriptionsAsync));
        return (OnListCustomerSubscriptions ?? throw NotConfigured(nameof(ListCustomerSubscriptionsAsync)))(customerReference, cancellationToken);
    }

    public Task<Subscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CreateSubscriptionAsync));
        return (OnCreateSubscription ?? throw NotConfigured(nameof(CreateSubscriptionAsync)))(customerReference, planHandle, cancellationToken);
    }

    public Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetSubscriptionAsync));
        return (OnGetSubscription ?? throw NotConfigured(nameof(GetSubscriptionAsync)))(subscriptionId, cancellationToken);
    }

    public Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(RecordUsageAsync));
        return (OnRecordUsage ?? throw NotConfigured(nameof(RecordUsageAsync)))(subscriptionId, quantity, memo, cancellationToken);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(PreviewPlanChangeAsync));
        return (OnPreviewPlanChange ?? throw NotConfigured(nameof(PreviewPlanChangeAsync)))(subscriptionId, targetPlanHandle, applyNow, cancellationToken);
    }

    public Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CommitPlanChangeAsync));
        return (OnCommitPlanChange ?? throw NotConfigured(nameof(CommitPlanChangeAsync)))(subscriptionId, targetPlanHandle, applyNow, cancellationToken);
    }

    public Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(PauseSubscriptionAsync));
        return (OnPause ?? throw NotConfigured(nameof(PauseSubscriptionAsync)))(subscriptionId, cancellationToken);
    }

    public Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ResumeSubscriptionAsync));
        return (OnResume ?? throw NotConfigured(nameof(ResumeSubscriptionAsync)))(subscriptionId, cancellationToken);
    }

    public Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CancelSubscriptionAsync));
        return (OnCancel ?? throw NotConfigured(nameof(CancelSubscriptionAsync)))(subscriptionId, endOfPeriod, reason, cancellationToken);
    }

    public Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ReactivateSubscriptionAsync));
        return (OnReactivate ?? throw NotConfigured(nameof(ReactivateSubscriptionAsync)))(subscriptionId, cancellationToken);
    }

    private static InvalidOperationException NotConfigured(string member) =>
        new($"FakeBillingClient.{member} was invoked but no behavior was configured for it.");
}
