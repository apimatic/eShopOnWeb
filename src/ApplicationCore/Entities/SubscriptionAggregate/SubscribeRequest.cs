using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The hero flow input: enroll <see cref="Subscriber"/> onto a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberProfile subscriber, string? planHandle, string? idempotencyKey = null)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        Subscriber = subscriber;
        PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle!.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
    }

    public SubscriberProfile Subscriber { get; }

    /// <summary>
    /// The stable API handle of the plan to subscribe to. When omitted, the configured default plan
    /// is used; if there is no configured default the request is rejected.
    /// </summary>
    public string? PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that makes the signup exactly replayable. When absent the resolved
    /// plan handle is used, which is what makes a double-clicked subscribe button a no-op.
    /// </summary>
    public string? IdempotencyKey { get; }
}
