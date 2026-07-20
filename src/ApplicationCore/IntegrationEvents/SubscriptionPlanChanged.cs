using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference, int subscriptionId, string oldProductHandle, string newProductHandle, bool appliedNow)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        AppliedNow = appliedNow;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public bool AppliedNow { get; }
}
