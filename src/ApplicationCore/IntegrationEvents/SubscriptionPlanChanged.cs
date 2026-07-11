using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a subscription's plan change (UC3) has been committed.</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(int subscriptionId, string buyerId, string previousProductHandle, string newProductHandle, bool immediate)
    {
        SubscriptionId = subscriptionId;
        BuyerId = buyerId;
        PreviousProductHandle = previousProductHandle;
        NewProductHandle = newProductHandle;
        Immediate = immediate;
    }

    public int SubscriptionId { get; }
    public string BuyerId { get; }
    public string PreviousProductHandle { get; }
    public string NewProductHandle { get; }
    public bool Immediate { get; }
}
