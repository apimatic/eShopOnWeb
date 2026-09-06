using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A snapshot of what the configured Maxio product family currently offers.
/// </summary>
/// <remarks>
/// Resolved as a unit and cached as a unit: the product family id is looked up from its handle at
/// runtime because Maxio reassigns numeric ids when a site is re-seeded, whereas handles are stable.
/// </remarks>
/// <param name="ProductFamilyId">Maxio id of the configured product family, resolved from its handle.</param>
/// <param name="Currency">ISO 4217 currency of the Maxio site, which is what product prices are quoted in.</param>
/// <param name="Plans">The non-archived products of the family, cheapest first.</param>
public record MaxioPlanCatalog(long ProductFamilyId, string Currency, IReadOnlyList<SubscriptionPlan> Plans);
