using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a customer was enrolled in a plan (UC1, step 6). Published in-process and
/// best-effort after the provider call succeeds — handlers that fail never roll back the
/// enrollment (§2.5).
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(Subscription subscription)
    {
        Subscription = subscription;
    }

    public Subscription Subscription { get; }
}
