using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the recurring-billing provider. This is the single interface
/// ApplicationCore knows about; the concrete provider (Maxio) is implemented once in Infrastructure
/// behind this contract. Every member throws <see cref="Exceptions.BillingProviderException"/> for
/// any provider-side failure (network, unexpected/error response) so callers only ever see one
/// failure shape.
/// </summary>
public interface IBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingPlan> GetPlanAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Verifies the configured metered component still resolves and is of metered kind.</summary>
    Task EnsureMeteredComponentIsValidAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up a customer by reference without creating one. Null if none exists yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Idempotent find-or-create keyed on <paramref name="reference"/>.</summary>
    Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, CancellationToken cancellationToken = default);

    /// <summary>The customer's currently active/trialing subscription, if any — used only to guard
    /// against double-enrollment (UC1); a paused/past-due subscription does not count as "active"
    /// here even though it is still the customer's subscription to manage.</summary>
    Task<Subscription?> FindActiveSubscriptionAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>The customer's most recent subscription regardless of state (active, paused,
    /// cancelled, ...), or null if they have never subscribed. Used to resolve "my subscription"
    /// for management (usage, plan change, lifecycle) — unlike <see cref="FindActiveSubscriptionAsync"/>,
    /// this must still find a paused or cancelled subscription so its owner can resume/reactivate it.</summary>
    Task<Subscription?> FindLatestSubscriptionAsync(int customerId, CancellationToken cancellationToken = default);

    Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingUsageBalance> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Preview of an immediate, prorated plan change.</summary>
    Task<BillingProrationPreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    /// <summary>Commits an immediate, prorated plan change.</summary>
    Task<Subscription> MigratePlanNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    /// <summary>Schedules a plan change to take effect at the next renewal, without proration.</summary>
    Task<Subscription> SchedulePlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
