using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Bridges eShopOnWeb shoppers to Maxio Advanced Billing, the system of record for
/// recurring-subscription billing. Maxio is treated as the source of truth: no
/// subscription state is duplicated locally.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerId"/> and enrolls them in
    /// <paramref name="productHandle"/>. Idempotent: if the buyer already has a live
    /// subscription to that plan, it is returned as-is rather than creating a duplicate.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(string buyerId, string buyerEmail, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every Maxio subscription (any state) belonging to the buyer.</summary>
    Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
