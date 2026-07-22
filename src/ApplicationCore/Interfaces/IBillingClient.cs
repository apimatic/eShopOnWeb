using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and whatever recurring-billing platform
/// runs the money. Exactly one implementation talks to the provider; nothing else in the
/// application may.
/// </summary>
/// <remarks>
/// <para>
/// Every member either returns a normalized domain type or throws
/// <see cref="Exceptions.BillingProviderException"/> (or its
/// <see cref="Exceptions.BillingConfigurationException"/> specialization). No provider SDK type,
/// status code, or transport exception escapes this interface.
/// </para>
/// <para>
/// All money crossing this seam is in whole currency units (dollars). Providers that speak in
/// minor units convert on the way through.
/// </para>
/// <para>
/// Lookups that ask "does this exist?" return <c>null</c> for a clean miss rather than throwing;
/// only genuine provider failures throw.
/// </para>
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Lists the plans customers may subscribe to, within the configured product family.
    /// Archived plans are excluded.
    /// </summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component and verifies it is of metered kind — the
    /// check UC2 runs at startup and again before the first usage call.
    /// </summary>
    /// <exception cref="Exceptions.BillingConfigurationException">
    /// The configured handle does not resolve, is archived, or resolves to a component of the wrong
    /// kind. Usage must not be recorded until the seed is corrected (plan.md UC0).
    /// </exception>
    Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the provider-side customer for an eShopOnWeb user reference.
    /// Returns null when the user has never been enrolled.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the provider-side customer for an eShopOnWeb user. The caller is responsible for
    /// checking <see cref="FindCustomerByReferenceAsync"/> first; this method always creates.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer, newest state as the provider sees it.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(BillingCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription. Returns null when the provider has no such subscription.</summary>
    Task<Subscription?> FindSubscriptionByIdAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls a customer in a plan.</summary>
    Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer,
        BillingPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>Records metered consumption against a subscription's component.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        MeteredComponent component,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums the units recorded against a component since the start of the subscription's current
    /// billing period.
    /// </summary>
    Task<int> GetPeriodToDateUsageAsync(Subscription subscription,
        MeteredComponent component,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what moving to <paramref name="targetPlan"/> would cost, without changing anything.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a subscription to another plan at the requested time.</summary>
    Task<Subscription> ChangePlanAsync(Subscription subscription,
        BillingPlan targetPlan,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold; no billing occurs until it is resumed.</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Takes a subscription off hold.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, immediately or at the end of the current period.</summary>
    Task<Subscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Brings a cancelled or expired subscription back to life.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
