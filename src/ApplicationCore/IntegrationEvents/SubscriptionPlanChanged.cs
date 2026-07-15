using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userId, int subscriptionId, string oldProductHandle, string newProductHandle)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
    }

    public string UserId { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
}
