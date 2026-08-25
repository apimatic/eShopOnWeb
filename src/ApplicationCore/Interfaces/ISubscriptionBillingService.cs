using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by the external billing system of record (Maxio).
/// All write operations are idempotent: retrying them never creates duplicate customers
/// or subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string? firstName, string? lastName,
        string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
