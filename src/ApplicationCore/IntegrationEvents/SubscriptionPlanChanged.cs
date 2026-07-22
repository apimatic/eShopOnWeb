using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a subscription is moved to a different plan (UC3).
/// </summary>
public class SubscriptionPlanChanged : INotification
{
    public SubscriptionPlanChanged(int subscriptionId, string oldProductHandle, string newProductHandle,
        CustomerSubscription subscription)
    {
        SubscriptionId = subscriptionId;
        OldProductHandle = oldProductHandle;
        NewProductHandle = newProductHandle;
        Subscription = subscription;
    }

    public int SubscriptionId { get; }

    public string OldProductHandle { get; }

    public string NewProductHandle { get; }

    public CustomerSubscription Subscription { get; }
}
