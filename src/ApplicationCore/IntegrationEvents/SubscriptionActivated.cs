using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(int subscriptionId, string userReference, string productHandle, string productName, int priceInCents)
    {
        SubscriptionId = subscriptionId;
        UserReference = userReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
    }

    public int SubscriptionId { get; }
    public string UserReference { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public int PriceInCents { get; }
}
