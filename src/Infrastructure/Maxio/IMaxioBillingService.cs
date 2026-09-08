using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Application-facing operations on the Maxio Advanced Billing site that backs
/// the eShopOnWeb subscription capability. Maxio is the billing system of record.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (products of the configured product family).
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single subscription plan by its handle, or null when it does not exist.
    /// </summary>
    Task<MaxioPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls a customer into a plan. Guarantees a Maxio customer exists for the
    /// given reference (creating it if needed) and that repeated calls with the same
    /// subscription reference never create a second active subscription.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(MaxioSubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Maxio subscriptions that belong to the customer with the given reference.
    /// Returns an empty list when no Maxio customer exists for the reference yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerReferenceAsync(string customerReference, CancellationToken cancellationToken = default);
}
