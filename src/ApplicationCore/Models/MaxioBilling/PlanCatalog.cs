using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;

/// <summary>
/// A subscription plan this application offers: one product of the configured Maxio product family.
/// </summary>
public record SubscriptionPlanInfo(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequireCreditCard);

/// <summary>
/// The plan catalogue as read from the billing system. <see cref="Truncated"/> is true when the
/// safety page cap stopped the walk before the provider signalled the last page — the list may be
/// incomplete.
/// </summary>
public record PlanCatalog(IReadOnlyList<SubscriptionPlanInfo> Plans, bool Truncated);