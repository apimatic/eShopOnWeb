using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam through which eShopOnWeb talks to the billing provider.
/// Exactly one Infrastructure class implements this against Maxio Advanced Billing; nothing above
/// ApplicationCore ever sees a provider-specific type. Every implementation must throw
/// <see cref="Exceptions.BillingProviderException"/> — never a provider SDK exception — on failure.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans a customer can subscribe to (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component and asserts it is of metered kind.
    /// Throws <see cref="Exceptions.BillingProviderException"/> with <see cref="Exceptions.BillingErrorKind.Validation"/>
    /// if the configured handle resolves to a non-metered component (UC2 preconditions).
    /// </summary>
    Task<BillingComponent> ValidateMeteredComponentAsync(CancellationToken ct = default);

    /// <summary>
    /// Idempotent lookup-or-create of the provider-side customer keyed on <paramref name="customerReference"/>
    /// (the eShopOnWeb user's email/username) — safe to call repeatedly for the same user (UC1 step 3).
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default);

    /// <summary>Looks up an existing provider-side customer by reference; returns null rather than creating one when none exists.</summary>
    Task<BillingCustomer?> FindCustomerAsync(string customerReference, CancellationToken ct = default);

    /// <summary>Every subscription the customer has ever had, across every product (UC1 duplicate-enrollment check).</summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);

    /// <summary>Enrolls the customer in the given plan (UC1 step 4).</summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string customerReference, string productHandle, CancellationToken ct = default);

    /// <summary>Re-reads a subscription's current state from the provider (used to recover after an ambiguous call).</summary>
    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Records usage against the configured metered component and reports the period-to-date total (UC2).</summary>
    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default);

    /// <summary>Previews the prorated cost/credit of an immediate plan change, without committing it (UC3).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Commits a plan change immediately, with proration (UC3, "apply now" timing).</summary>
    Task<Subscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Schedules a plan change to take effect at the next renewal, without proration (UC3, "at next renewal" timing).</summary>
    Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Puts the subscription on indefinite hold (UC4).</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Takes the subscription off hold, back to active (UC4).</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Cancels the subscription, immediately or at the end of the current period (UC4).</summary>
    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, CancellationToken ct = default);

    /// <summary>Reactivates a cancelled/unpaid/trial-ended subscription (UC4).</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
