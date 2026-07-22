using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user was enrolled in a plan. Published in-process, best-effort, only
/// after the provider has confirmed the enrollment.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, CustomerSubscription subscription)
    {
        UserReference = userReference;
        Subscription = subscription;
    }

    public string UserReference { get; }

    public CustomerSubscription Subscription { get; }
}
