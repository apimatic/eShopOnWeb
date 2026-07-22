using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam between eShopOnWeb and the recurring-billing provider. Exactly one
/// implementation talks to the provider; nothing else in the application does.
/// </summary>
/// <remarks>
/// Every member either returns a normalized domain type or throws
/// <see cref="Exceptions.BillingProviderException"/> (provider refused or unreachable) or
/// <see cref="Exceptions.BillingConfigurationException"/> (configured entity does not resolve).
/// No provider-specific exception, model, or unit escapes this interface, and all money is
/// expressed in whole currency units — never cents.
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its stable handle, or <c>null</c> when no such plan exists.</summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the metered component by its stable handle, or <c>null</c> when no such component
    /// exists. Callers must check <see cref="BillingComponent.IsMetered"/> before reporting usage.
    /// </summary>
    Task<BillingComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Looks up the provider customer for an eShopOnWeb user, or <c>null</c> when none exists yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for an eShopOnWeb user, creating it when absent. Idempotent on
    /// <paramref name="customerReference"/>, so repeated calls never create a second customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by a provider customer, in any state.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription. Throws when the subscription does not exist.</summary>
    Task<Subscription> GetSubscriptionAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls a customer in a plan.</summary>
    Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Reports metered consumption against a subscription's component.</summary>
    Task<UsageRecord> RecordUsageAsync(int providerSubscriptionId,
        BillingComponent component,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the units accrued against a component in the current billing period, or <c>null</c>
    /// when the provider has no balance to report for it yet.
    /// </summary>
    Task<int?> GetPeriodToDateUnitsAsync(int providerSubscriptionId,
        BillingComponent component,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the cost of a plan change without committing anything.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the given timing.</summary>
    Task<Subscription> ChangePlanAsync(Subscription subscription,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Puts billing on hold. The subscription can later be resumed.</summary>
    Task<Subscription> PauseSubscriptionAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Takes a paused subscription off hold.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either at once or at the end of the current period.</summary>
    Task<Subscription> CancelSubscriptionAsync(int providerSubscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Brings a cancelled or expired subscription back to life.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);
}
