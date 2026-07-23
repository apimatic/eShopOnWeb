using System;
using MediatR;

using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition carrying old to new state (UC4 step 3). Published in-process
/// after the provider applied the transition; delivery is best-effort (§2.5).
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string buyerId,
        long subscriptionId,
        SubscriptionState previousState,
        SubscriptionState newState,
        string action,
        DateTimeOffset? effectiveAt,
        string? reason)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        PreviousState = previousState;
        NewState = newState;
        Action = action;
        EffectiveAt = effectiveAt;
        Reason = reason;
    }

    public string BuyerId { get; }

    public long SubscriptionId { get; }

    public SubscriptionState PreviousState { get; }

    public SubscriptionState NewState { get; }

    /// <summary>The requested action: pause, resume, cancel or reactivate.</summary>
    public string Action { get; }

    /// <summary>
    /// When the transition takes effect. For an end-of-period cancel this is the period boundary,
    /// not the moment the request was made.
    /// </summary>
    public DateTimeOffset? EffectiveAt { get; }

    public string? Reason { get; }
}
