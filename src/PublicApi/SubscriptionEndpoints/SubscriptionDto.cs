using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as surfaced to API consumers.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The Maxio id of the owning customer.</summary>
    public int CustomerId { get; set; }

    /// <summary>Current subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Handle of the subscribed plan.</summary>
    public string? ProductHandle { get; set; }

    /// <summary>Name of the subscribed plan.</summary>
    public string? ProductName { get; set; }

    /// <summary>Recurring price of the subscription, in integer cents.</summary>
    public long ProductPriceInCents { get; set; }

    public int? ProductPricePointId { get; set; }

    /// <summary>Next billing date (end of the current period).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? Currency { get; set; }

    public string? Reference { get; set; }
}
