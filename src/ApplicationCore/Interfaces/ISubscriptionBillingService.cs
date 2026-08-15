using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, abstracted over the billing provider. Implementations translate
/// provider failures into <c>Microsoft.eShopWeb.Infrastructure.Maxio.MaxioBillingException</c> so the
/// API layer sees a single failure type carrying an HTTP-mappable status.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls the subscriber in a plan: ensures a provider customer exists for the
    /// user (create-if-missing), then ensures a subscription to the plan exists (reuse-if-present),
    /// so a repeated/double-clicked call never creates a duplicate customer or subscription.
    /// </summary>
    /// <param name="subscriber">The authenticated caller (identity is server-derived, never client-supplied).</param>
    /// <param name="planHandle">Target plan handle; when null/blank the configured default plan is used.</param>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriber's subscriptions; empty when the user has no provider customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
