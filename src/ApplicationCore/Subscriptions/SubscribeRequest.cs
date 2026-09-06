using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's request to start a subscription.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(Subscriber subscriber, string? planHandle = null, string? idempotencyKey = null)
    {
        Subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        PlanHandle = string.IsNullOrWhiteSpace(planHandle) ? null : planHandle.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
    }

    public Subscriber Subscriber { get; }

    /// <summary>Plan to subscribe to. When null the configured default plan is used.</summary>
    public string? PlanHandle { get; }

    /// <summary>
    /// Caller-supplied key that makes retries of one logical subscribe attempt safe. When omitted the
    /// integration derives its own, so an unkeyed double-click still collapses into one subscription.
    /// </summary>
    public string? IdempotencyKey { get; }
}
