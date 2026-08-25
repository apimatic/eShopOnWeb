using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates subscription billing against Maxio Advanced Billing, which is the
/// billing system of record. Customers are correlated to eShopOnWeb users via the
/// Maxio customer reference (the eShopOnWeb username), so no local mapping table
/// is required.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the purchasable plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanModel>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes a user to a plan: ensures a Maxio customer exists for the
    /// user, then creates the subscription unless a live subscription to the same plan
    /// already exists (in which case the existing one is returned).
    /// </summary>
    Task<SubscribeResultModel> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions for the user's Maxio customer record.</summary>
    Task<IReadOnlyList<SubscriptionModel>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
