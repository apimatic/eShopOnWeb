using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam to the recurring billing engine. This is the only abstraction the
/// domain knows about; exactly one Infrastructure implementation talks to the provider (plan §2.2).
/// </summary>
/// <remarks>
/// Implementations translate every provider failure into the billing exception family in
/// <c>ApplicationCore.Exceptions</c> and normalise all money into major currency units.
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the non-archived recurring plans available in the configured product family.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a single plan by its stable handle, or <c>null</c> when no such plan exists.</summary>
    Task<BillingPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Looks a customer up by the eShopOnWeb user reference, or <c>null</c> when unknown.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates the provider-side customer record for an eShopOnWeb user.</summary>
    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Enrols an existing customer in a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription, or <c>null</c> when the provider has no such subscription.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the provider holds for a customer.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component and proves it is of metered kind on the
    /// configured product family (UC2 precondition).
    /// </summary>
    /// <exception cref="Exceptions.BillingConfigurationException">
    /// The configured handle does not resolve on the family, or resolves to a non-metered component.
    /// </exception>
    Task<BillingComponent> GetUsageComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports consumption against the configured metered component on a subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date unit balance for a component on a subscription, or <c>null</c>
    /// when the provider does not report one.
    /// </summary>
    Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, int componentId,
        CancellationToken cancellationToken = default);

    /// <summary>Quotes the prorated cost of moving a subscription onto another plan. Commits nothing.</summary>
    Task<PlanMigrationQuote> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a subscription onto another plan immediately, prorating the difference.</summary>
    Task<BillingSubscription> MigratePlanAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a plan change for the next renewal. No proration applies.</summary>
    Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold.</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Releases a subscription from hold.</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription straight away.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Defers cancellation to the end of the current billing period and returns the refreshed
    /// subscription as the provider now reports it.
    /// </summary>
    Task<BillingSubscription> ScheduleCancellationAsync(int subscriptionId, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Brings a cancelled or expired subscription back to life.</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
