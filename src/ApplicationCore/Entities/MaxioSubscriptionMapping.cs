using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Stores the relationship between an eShopOnWeb user and the corresponding Maxio subscription.
/// Maxio remains the billing system of record; this entity is only the durable correlation key.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public int MaxioSubscriptionId { get; set; }

    public string SubscriptionReference { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
