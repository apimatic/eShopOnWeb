using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userId, int subscriptionId, string productHandle)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        ProductHandle = productHandle;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public string ProductHandle { get; }
}
