using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process after a customer's subscription is successfully enrolled (UC1).</summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(int subscriptionId, string buyerId, string productHandle)
    {
        SubscriptionId = subscriptionId;
        BuyerId = buyerId;
        ProductHandle = productHandle;
    }

    public int SubscriptionId { get; }
    public string BuyerId { get; }
    public string ProductHandle { get; }
}
