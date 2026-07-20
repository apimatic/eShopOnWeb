using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, int subscriptionId, string productHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
}
