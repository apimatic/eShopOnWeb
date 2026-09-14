using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The eShopOnWeb-facing billing abstraction over Maxio Advanced Billing. Every implementation
/// translates SDK failures into <see cref="MaxioBillingException"/>, so callers handle one error type.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the subscription plans (products) in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper (idempotent by <see cref="ShopperIdentity.Reference"/>)
    /// and enrolls them in <paramref name="productHandle"/>. Idempotent against double-submits: an existing
    /// active subscription to the same plan is returned rather than duplicated.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken ct);

    /// <summary>Lists the shopper's subscriptions. Returns empty when the shopper has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken ct);
}
