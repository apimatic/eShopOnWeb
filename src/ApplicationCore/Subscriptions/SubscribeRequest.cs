using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll <see cref="Subscriber"/> in the plan identified by <see cref="PlanHandle"/>.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        Subscriber = subscriber;
        PlanHandle = planHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    /// <summary>Handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that scopes the enrollment. Two calls with the same subscriber,
    /// plan and key always resolve to the same subscription. Omit it to get one subscription per
    /// subscriber and plan, which is what makes a double-clicked Subscribe button safe.
    /// </summary>
    public string? IdempotencyKey { get; }
}
