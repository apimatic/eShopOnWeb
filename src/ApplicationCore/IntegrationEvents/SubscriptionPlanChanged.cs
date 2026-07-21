using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a subscription's plan change commits, immediate or delayed (UC3).</summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(string userReference, int subscriptionId, string oldProductHandle, string newProductHandle, bool effectiveImmediately)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        EffectiveImmediately = effectiveImmediately;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public string OldProductHandle { get; }
    public string NewProductHandle { get; }
    public bool EffectiveImmediately { get; }
}
