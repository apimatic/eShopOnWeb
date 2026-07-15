using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort — plan.md §2.5) after UC3 commits a plan change.
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(int subscriptionId, string previousProductHandle, string newProductHandle)
    {
        SubscriptionId = subscriptionId;
        PreviousProductHandle = previousProductHandle;
        NewProductHandle = newProductHandle;
    }

    public int SubscriptionId { get; }
    public string PreviousProductHandle { get; }
    public string NewProductHandle { get; }
}
