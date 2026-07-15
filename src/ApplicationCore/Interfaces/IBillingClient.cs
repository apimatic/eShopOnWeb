using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic abstraction over the billing provider (Maxio Advanced Billing). This is the single
/// seam ApplicationCore uses to reach the billing provider — ApplicationCore must never reference the
/// provider's SDK or HttpClient directly (plan.md §2.2). The concrete implementation lives in
/// Infrastructure (<c>MaxioBillingClient</c>) and is the only class that talks to the provider.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available for subscription.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a single plan by its stable handle. Returns null if the handle does not resolve.</summary>
    Task<BillingPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the configured metered component resolves and is of metered kind (plan.md UC2 startup
    /// validation). Throws <see cref="Exceptions.BillingConfigurationException"/> if it does not.
    /// </summary>
    Task EnsureMeteredComponentConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the customer for the given stable reference. Returns null if none exists yet.</summary>
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Finds the customer for the given stable reference, or creates one if none exists yet.</summary>
    Task<BillingCustomer> FindOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription owned by the given billing-provider customer.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription's current state directly from the provider.</summary>
    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls the given customer in the given plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Records a quantity of metered usage against the subscription's configured component.</summary>
    Task<BillingUsageReading> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Previews the cost impact of moving the subscription to a different plan.</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>Commits a previously previewed plan change.</summary>
    Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>Pauses an active subscription.</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused subscription.</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current billing period.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription.</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
