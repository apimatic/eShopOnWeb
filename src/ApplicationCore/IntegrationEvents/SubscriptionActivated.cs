using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a customer has been successfully enrolled in a plan (UC1). Delivery is
/// in-process and best-effort: a failing handler never rolls the enrollment back.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userName, Subscription subscription)
    {
        UserName = userName;
        Subscription = subscription;
    }

    public string UserName { get; }
    public Subscription Subscription { get; }
}
