using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam between eShopOnWeb and whatever recurring-billing platform is in use.
/// Exactly one Infrastructure implementation talks to the provider (plan.md §2.2); nothing above this
/// interface knows the provider's transport, models, or vocabulary.
/// </summary>
/// <remarks>
/// Contract every implementation must honour:
/// <list type="bullet">
/// <item>All money is expressed in dollars, never in the provider's minor units.</item>
/// <item>Lookups that can legitimately miss (<c>Find…</c>, <c>Get…</c>) return <see langword="null"/>
/// rather than throwing, so callers can distinguish "not there" from "provider failed".</item>
/// <item>Every provider-side failure surfaces as
/// <see cref="Exceptions.BillingProviderException"/> — implementations must not leak provider
/// or transport exception types.</item>
/// </list>
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle, or <see langword="null"/> when no such plan exists.</summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a component on the configured product family by handle, or <see langword="null"/> when
    /// no such component exists. Callers must check <see cref="MeteredComponent.IsMetered"/> before
    /// recording usage (plan.md UC2).
    /// </summary>
    Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider-side customer for an eShopOnWeb user reference, or <see langword="null"/>.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Creates a provider-side customer.</summary>
    Task<BillingCustomer> CreateCustomerAsync(BillingCustomerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in the plan identified by <paramref name="planHandle"/>.</summary>
    Task<Subscription> CreateSubscriptionAsync(BillingCustomer customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider-side customer; empty when there are none.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription by id, or <see langword="null"/> when the id is unknown.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against a subscription's component.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, int quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date unit total for a component on a subscription, or
    /// <see langword="null"/> when the provider does not report one.
    /// </summary>
    Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Computes the prorated cost of moving a subscription to another plan, charging nothing.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the requested timing and returns the refreshed subscription.</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition and returns the refreshed subscription.</summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, string? reason,
        CancellationToken cancellationToken = default);
}
