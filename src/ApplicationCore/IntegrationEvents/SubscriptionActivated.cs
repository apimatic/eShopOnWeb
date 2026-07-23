using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer has been successfully enrolled in a plan.
/// </summary>
/// <remarks>
/// Delivery is best-effort and in-process only: eShopOnWeb has no broker and no outbox, so a
/// handler failure never rolls back the enrollment that already succeeded.
/// </remarks>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, BillingSubscription subscription)
    {
        UserReference = userReference;
        Subscription = subscription;
    }

    /// <summary>The eShopOnWeb user reference (email / username) that owns the subscription.</summary>
    public string UserReference { get; }

    public BillingSubscription Subscription { get; }
}
