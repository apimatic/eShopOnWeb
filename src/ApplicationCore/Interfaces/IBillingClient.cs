using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam onto the recurring-billing platform. Exactly one implementation
/// lives in Infrastructure and is the only code in the solution that talks to the provider;
/// everything above this interface reasons in eShopOnWeb's own domain types.
/// </summary>
/// <remarks>
/// Every member surfaces provider failures as
/// <see cref="Exceptions.BillingProviderException"/> — no provider-specific exception type is
/// allowed to cross this boundary.
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Lists the live recurring plans available to subscribe to, cheapest first. Archived plans
    /// are excluded. Returns an empty collection when the configured family holds no plans.
    /// </summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a plan by its stable handle, or <see langword="null"/> when no such plan exists.
    /// </summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component by handle, or <see langword="null"/> when
    /// it does not exist on the configured family. The returned
    /// <see cref="MeteredComponent.IsMetered"/> flag lets the caller refuse to record usage
    /// against a component of the wrong kind.
    /// </summary>
    Task<MeteredComponent?> FindMeteredComponentAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the pay-as-you-go component this deployment is configured to bill against, having
    /// verified that it exists, is not archived, and really is of metered kind (UC2's
    /// precondition). Throws <see cref="Exceptions.BillingConfigurationException"/> when the seed
    /// does not match the configuration, so usage is never recorded against the wrong component.
    /// The successful result is cached for the lifetime of the client.
    /// </summary>
    Task<MeteredComponent> GetConfiguredMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the provider-side customer for an eShopOnWeb user reference, or
    /// <see langword="null"/> when none exists yet.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for an eShopOnWeb user, creating it if it does not yet
    /// exist. Idempotent on <see cref="BillingCustomerRegistration.Reference"/>, so a retried or
    /// double-clicked subscribe never produces a duplicate customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in a plan identified by its stable handle.</summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer, newest first. Returns an empty
    /// collection when the customer has never subscribed.
    /// </summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription, or <see langword="null"/> when the id is unknown to the
    /// provider.
    /// </summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records consumed units against a subscription's metered component.</summary>
    Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        int componentId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums the units recorded against a component within the given window — used to show the
    /// running period-to-date total after a usage report.
    /// </summary>
    Task<decimal> GetPeriodToDateUsageAsync(
        int subscriptionId,
        int componentId,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what moving a subscription to another plan would cost, without applying it.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a subscription to another plan at the requested effective time.</summary>
    Task<Subscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold indefinitely.</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Takes a subscription off hold and resumes billing.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately or at the end of the paid period.</summary>
    Task<Subscription> CancelSubscriptionAsync(
        int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Brings a lapsed subscription back to life.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
