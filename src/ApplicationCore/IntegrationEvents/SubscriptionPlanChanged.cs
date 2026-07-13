using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(int subscriptionId, string userReference, string oldProductHandle, string newProductHandle, bool appliedNow, int? prorationAmountInCents)
    {
        SubscriptionId = subscriptionId;
        UserReference = userReference;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        AppliedNow = appliedNow;
        ProrationAmountInCents = prorationAmountInCents;
    }

    public int SubscriptionId { get; }
    public string UserReference { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public bool AppliedNow { get; }
    public int? ProrationAmountInCents { get; }
}
