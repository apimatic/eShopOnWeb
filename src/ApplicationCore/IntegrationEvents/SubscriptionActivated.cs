using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process (best-effort, §2.5) after a customer is successfully enrolled in a plan (UC1).
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userName, CustomerSubscription subscription)
    {
        UserName = userName;
        Subscription = subscription;
    }

    public string UserName { get; }

    public CustomerSubscription Subscription { get; }
}
