using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Command to enroll <see cref="Subscriber"/> in the plan identified by <see cref="PlanHandle"/>.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null)
    {
        Subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));

        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));

        PlanHandle = planHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key. Two requests carrying the same key are guaranteed to produce
    /// at most one subscription. When omitted the subscriber + plan pair is used instead, which
    /// already makes an accidental double submit safe.
    /// </summary>
    public string? IdempotencyKey { get; }
}
