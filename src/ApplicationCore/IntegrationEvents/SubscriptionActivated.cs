using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user was enrolled in a plan. Published in-process, best-effort,
/// only after the provider call succeeded — a failing handler never rolls the enrollment back.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, BillingSubscription subscription)
    {
        UserReference = userReference;
        Subscription = subscription;
    }

    /// <summary>The eShopOnWeb user this subscription belongs to.</summary>
    public string UserReference { get; }

    public BillingSubscription Subscription { get; }
}
