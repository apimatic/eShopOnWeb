using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer has been successfully enrolled in a plan (UC1, step 6).
/// Delivery is best-effort: a failing handler never rolls back the enrollment.
/// </summary>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userReference, CustomerSubscription subscription)
    {
        UserReference = userReference;
        Subscription = subscription;
    }

    /// <summary>The stable eShopOnWeb user reference (email/username) that was enrolled.</summary>
    public string UserReference { get; }

    public CustomerSubscription Subscription { get; }
}
