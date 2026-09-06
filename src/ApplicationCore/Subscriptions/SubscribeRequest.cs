using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll a shopper on a recurring plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(Subscriber subscriber, string planHandle, string? pricePointHandle = null, string? idempotencyKey = null)
    {
        Subscriber = Guard.Against.Null(subscriber, nameof(subscriber));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        PricePointHandle = string.IsNullOrWhiteSpace(pricePointHandle) ? null : pricePointHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public Subscriber Subscriber { get; }

    /// <summary>The plan's stable API handle (billing numeric ids are not stable across re-seeds).</summary>
    public string PlanHandle { get; }

    /// <summary>Optional non-default price point handle for the plan.</summary>
    public string? PricePointHandle { get; }

    /// <summary>
    /// Optional caller supplied key. Two requests carrying the same key for the same shopper
    /// resolve to the same subscription instead of creating a second one.
    /// </summary>
    public string? IdempotencyKey { get; }
}
