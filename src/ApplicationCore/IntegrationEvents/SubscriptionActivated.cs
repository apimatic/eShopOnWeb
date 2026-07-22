using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a customer has been successfully enrolled in a plan (UC1 step 6).
/// </summary>
/// <remarks>
/// Delivery is in-process and best-effort (plan.md §2.5). A handler that throws does not roll the
/// enrolment back — the subscription already exists at the provider.
/// </remarks>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(Subscription subscription)
    {
        Subscription = subscription;
    }

    public Subscription Subscription { get; }
}
