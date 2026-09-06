using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command to enroll a shopper on a plan.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(SubscriberIdentity subscriber, string planHandle, string? idempotencyKey = null)
    {
        Subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        PlanHandle = planHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
    }

    public SubscriberIdentity Subscriber { get; }

    /// <summary>Handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key that makes the subscribe call safely retryable across processes.
    /// When omitted the billing layer still de-duplicates against the shopper's live subscriptions.
    /// </summary>
    public string? IdempotencyKey { get; }
}
