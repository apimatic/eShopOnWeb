namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Enrolls a <see cref="Subscriber"/> in the plan identified by <see cref="PlanHandle"/>.
/// </summary>
public class SubscribeRequest
{
    public required Subscriber Subscriber { get; init; }

    public required string PlanHandle { get; init; }

    /// <summary>
    /// Optional caller supplied key. When present it is forwarded to the billing system so
    /// that a retry of the same logical request is rejected there rather than creating a
    /// second subscription, even if the retry lands on a different application instance.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
