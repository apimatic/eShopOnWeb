using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a subscription's plan change commits (UC3).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }

    public SubscriptionPlanChanged(string userReference, int subscriptionId, string oldProductHandle, string newProductHandle)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
    }
}
