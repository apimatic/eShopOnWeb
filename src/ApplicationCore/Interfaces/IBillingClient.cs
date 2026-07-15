using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// The single seam between eShopOnWeb and the billing provider. Only the Infrastructure
// implementation of this interface (MaxioBillingClient) may reference the provider SDK.
// Every member throws Microsoft.eShopWeb.ApplicationCore.Exceptions.BillingProviderException
// on any provider-side or connection-level failure.
public interface IBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    // Confirms the configured metered component still resolves to a component of metered kind.
    // Cached after the first successful check for the lifetime of the process.
    Task EnsureMeteredComponentIsValidAsync(CancellationToken ct = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken ct = default);

    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName,
        string lastName, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId,
        CancellationToken ct = default);

    Task<CustomerSubscription> CreateSubscriptionAsync(int billingCustomerId, string planHandle,
        CancellationToken ct = default);

    Task<CustomerSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, double quantity, string? memo,
        CancellationToken ct = default);

    // Null when the read-back fails; the usage record itself already stands by the time this is called.
    Task<int?> TryGetMeteredComponentBalanceAsync(int subscriptionId, CancellationToken ct = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken ct = default);

    Task<CustomerSubscription> ApplyPlanChangeNowAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken ct = default);

    Task<CustomerSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken ct = default);

    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason, bool endOfPeriod,
        CancellationToken ct = default);

    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
