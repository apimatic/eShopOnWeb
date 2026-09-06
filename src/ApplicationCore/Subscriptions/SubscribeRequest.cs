using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll <see cref="Subscriber"/> on a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string? planHandle = null)
    {
        Subscriber = Guard.Against.Null(subscriber, nameof(subscriber));
        PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    /// <summary>
    /// Plan to enroll on. When null the configured default plan is used; if none is
    /// configured the request is rejected rather than guessing on the shopper's behalf.
    /// </summary>
    public string? PlanHandle { get; }
}
