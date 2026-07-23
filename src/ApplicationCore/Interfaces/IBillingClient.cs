using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam to the recurring-billing engine. Exactly one Infrastructure class
/// implements this; nothing else in eShopOnWeb talks to the billing provider.
/// </summary>
/// <remarks>
/// Every member throws
/// <see cref="ApplicationCore.Exceptions.BillingProviderException"/> when the provider rejects or
/// cannot serve the request, so callers never see a provider-specific exception type.
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle, or null when the handle does not resolve.</summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component on the product family. Used by the UC2
    /// validation that refuses to record usage against a non-metered component.
    /// </summary>
    Task<MeteredComponentDefinition> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for this reference, creating one if it does not exist.
    /// Idempotent on <see cref="CustomerRegistration.Reference"/>.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(CustomerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Looks a customer up by reference, or null when no such customer exists.</summary>
    Task<BillingCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to the customer with this reference.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or null when the id is unknown to the provider.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls the customer with this reference in the plan with this handle.</summary>
    Task<Subscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against the configured component on this subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the accumulated metered units for the subscription's current billing period, or null
    /// when the provider cannot supply it.
    /// </summary>
    Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Computes the cost of moving this subscription to another plan, without committing.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the requested timing.</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Puts the subscription on hold.</summary>
    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Takes the subscription off hold.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the subscription, immediately or at the end of the current period.</summary>
    Task<Subscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a canceled subscription.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
