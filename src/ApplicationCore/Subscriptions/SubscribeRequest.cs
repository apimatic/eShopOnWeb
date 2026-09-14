using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command to enroll a subscriber onto a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string? planHandle, string? idempotencyKey = null)
    {
        Subscriber = Guard.Against.Null(subscriber, nameof(subscriber));
        PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle!.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    /// <summary>
    /// Plan to enroll on. When null the configured default plan is used; the request fails with a
    /// plan-not-found error if no default is configured.
    /// </summary>
    public string? PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that scopes duplicate suppression. Omitting it still de-duplicates
    /// on (subscriber, plan), which is what makes a double-clicked Subscribe button safe.
    /// </summary>
    public string? IdempotencyKey { get; }
}
