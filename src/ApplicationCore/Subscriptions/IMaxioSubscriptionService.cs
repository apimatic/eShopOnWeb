using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Application-facing abstraction over Maxio Advanced Billing for the recurring-subscription
/// capability. Implemented against the Maxio OpenAPI contract in the Infrastructure layer.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrolment (the products in the configured
    /// product family).
    /// </summary>
    Task<Result<IReadOnlyCollection<SubscriptionPlan>>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the subscriber (idempotent) and enrols them on the
    /// requested plan. If a matching live subscription already exists it is returned unchanged
    /// rather than creating a duplicate.
    /// </summary>
    Task<Result<CustomerSubscription>> SubscribeAsync(EShopSubscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriber's subscriptions. Returns an empty collection when the subscriber has
    /// no Maxio customer record yet.
    /// </summary>
    Task<Result<IReadOnlyCollection<CustomerSubscription>>> GetSubscriptionsAsync(EShopSubscriber subscriber, CancellationToken cancellationToken = default);
}
