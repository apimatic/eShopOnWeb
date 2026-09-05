using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Application-facing façade over Maxio Advanced Billing - Maxio is the system of record for
/// everything subscription-related, so this service never persists billing state locally.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the subscribable plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the caller and enrolls them in the requested plan.
    /// Idempotent: re-invoking with the same <see cref="SubscribeRequest.UserReference"/> and
    /// <see cref="SubscribeRequest.PlanHandle"/> will not create a duplicate customer or a
    /// duplicate active subscription.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription (any state) belonging to the given user.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
