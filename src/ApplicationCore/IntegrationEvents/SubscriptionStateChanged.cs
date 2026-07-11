using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userId, int subscriptionId, string fromState,
        string toState, DateTime effectiveDate, string? reason = null)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        FromState = fromState;
        ToState = toState;
        EffectiveDate = effectiveDate;
        Reason = reason;
    }

    public string UserId { get; set; }
    public int SubscriptionId { get; set; }
    public string FromState { get; set; }
    public string ToState { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? Reason { get; set; }
}
