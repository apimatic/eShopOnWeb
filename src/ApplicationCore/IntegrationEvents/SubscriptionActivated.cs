using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user has been enrolled in a plan (UC1). Published best-effort and
/// in-process after the provider call has succeeded; a handler failure never undoes the enrollment.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(Subscription subscription)
    {
        Subscription = subscription;
    }

    public Subscription Subscription { get; }
}
