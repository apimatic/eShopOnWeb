using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a lifecycle transition (pause/resume/cancel/reactivate) commits (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public int SubscriptionId { get; }
    public string UserName { get; }
    public SubscriptionState OldState { get; }
    public SubscriptionState NewState { get; }
    public DateTimeOffset EffectiveAt { get; }

    public SubscriptionStateChanged(int subscriptionId, string userName, SubscriptionState oldState, SubscriptionState newState, DateTimeOffset effectiveAt)
    {
        SubscriptionId = subscriptionId;
        UserName = userName;
        OldState = oldState;
        NewState = newState;
        EffectiveAt = effectiveAt;
    }
}
