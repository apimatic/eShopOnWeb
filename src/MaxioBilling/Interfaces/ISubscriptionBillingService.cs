using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Models;

namespace Microsoft.eShopWeb.MaxioBilling.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// Every method throws <see cref="BillingException"/> — and nothing else — for any provider,
/// transport or configuration failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>True when the <c>Maxio</c> configuration section is complete enough to serve requests.</summary>
    bool IsConfigured { get; }

    /// <summary>Lists the purchasable plans of the configured product family, newest catalog state first read.</summary>
    Task<IReadOnlyList<PlanSummary>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="subscriber"/> and enrolls them on a plan.
    /// Idempotent: repeating the call returns the existing live subscription instead of creating a second.
    /// </summary>
    /// <param name="planHandle">
    /// Plan to subscribe to. When null or empty the configured default plan handle is used.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions Maxio holds for <paramref name="subscriber"/>; empty when they have none.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
