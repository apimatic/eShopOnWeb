namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A request to enroll <see cref="Subscriber"/> onto the plan identified by <see cref="PlanHandle"/>.
/// </summary>
public class SubscribeRequest
{
    public required Subscriber Subscriber { get; init; }

    /// <summary>Handle of the plan to subscribe to; when null the configured default plan is used.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>
    /// Optional caller supplied idempotency key. Two subscribe calls carrying the same key
    /// for the same subscriber resolve to the same subscription. When omitted, the
    /// subscriber + plan pair is used as the idempotency scope.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
