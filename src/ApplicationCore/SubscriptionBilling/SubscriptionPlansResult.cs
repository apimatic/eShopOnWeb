using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// The set of subscribable plans. <see cref="Truncated"/> tells the caller the list was capped by the
/// page size and may be incomplete — a partial answer the caller can detect rather than mistake for a
/// complete one.
/// </summary>
public record SubscriptionPlansResult
{
    public required IReadOnlyList<SubscriptionPlanInfo> Plans { get; init; }
    public bool Truncated { get; init; }
}
