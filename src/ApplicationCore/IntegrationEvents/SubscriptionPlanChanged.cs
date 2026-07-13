using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, after a subscription's plan change has been committed (UC3).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string customerReference, int subscriptionId, string oldProductHandle, string newProductHandle)
    {
        CustomerReference = customerReference;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
    }

    public string CustomerReference { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
}
