namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll <see cref="Subscriber"/> onto the plan identified by <see cref="PlanHandle"/>.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(Subscriber subscriber, string planHandle, string? idempotencyKey = null)
    {
        Subscriber = subscriber;
        PlanHandle = planHandle;
        IdempotencyKey = idempotencyKey;
    }

    public Subscriber Subscriber { get; }

    /// <summary>Stable handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that scopes idempotency. When omitted, the plan handle is
    /// used, so repeated submissions for the same subscriber and plan collapse into a single
    /// subscription. Supply a distinct value to deliberately create an additional subscription,
    /// for example when re-subscribing after a cancellation.
    /// </summary>
    public string? IdempotencyKey { get; }
}
