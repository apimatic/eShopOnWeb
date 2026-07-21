using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a lifecycle transition - pause/resume/cancel/reactivate (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string customerReference, int subscriptionId, BillingSubscriptionState oldState, BillingSubscriptionState newState)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public BillingSubscriptionState OldState { get; }
    public BillingSubscriptionState NewState { get; }
}
