using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer is successfully enrolled in a plan (UC1 step 6).
/// Delivery is best-effort: a failing handler never rolls the enrolment back (plan.md §2.5).
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(Subscription subscription)
    {
        Subscription = subscription;
    }

    public Subscription Subscription { get; }
}
