using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published in-process (best-effort, §2.5) after a subscription is successfully enrolled (UC1).</summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string buyerId, int subscriptionId, string productHandle, string productName, long priceInCents)
    {
        BuyerId = buyerId;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
    }

    public string BuyerId { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
}
