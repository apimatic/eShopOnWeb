using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by the billing system of record (Maxio).
/// The shopper is identified by <c>userReference</c> — the stable eShopOnWeb username —
/// which is mirrored onto the billing customer record so enrollment is idempotent.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string userReference, string userEmail, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
