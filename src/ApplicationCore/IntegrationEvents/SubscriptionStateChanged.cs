using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription lifecycle transition (UC4: pause,
/// resume, cancel, reactivate) is applied. See §2.5 of the integration plan.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference, int subscriptionId, BillingSubscriptionState oldState, BillingSubscriptionState newState)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldState = oldState;
        NewState = newState;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public BillingSubscriptionState OldState { get; }
    public BillingSubscriptionState NewState { get; }
}
