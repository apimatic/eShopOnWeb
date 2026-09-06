using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command to enrol a shopper on a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null)
    {
        Subscriber = Guard.Against.Null(subscriber, nameof(subscriber));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle)).Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    /// <summary>Handle of the plan to subscribe to, as returned by the plan list.</summary>
    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key. Two requests carrying the same key for the same shopper are
    /// guaranteed to produce a single subscription, even across process restarts, because the key is
    /// folded into the subscription reference the provider enforces uniqueness on.
    /// </summary>
    public string? IdempotencyKey { get; }
}
