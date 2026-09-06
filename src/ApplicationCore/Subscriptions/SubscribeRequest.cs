using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll a <see cref="Subscriptions.Subscriber"/> on a plan.
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

    /// <summary>The plan to enroll on. When null, the configured default plan is used.</summary>
    public string? PlanHandle { get; }

    /// <summary>
    /// Caller supplied replay key. When supplied, repeating the request with the same key returns the
    /// subscription created by the first call instead of creating a second one.
    /// </summary>
    public string? IdempotencyKey { get; }
}
