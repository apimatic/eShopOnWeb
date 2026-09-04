using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The durable link between an eShopOnWeb identity and a Maxio subscription.
/// Maxio remains the source of truth for the subscription's current state.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public int MaxioSubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public DateTimeOffset LastSeenAt { get; set; }
}
