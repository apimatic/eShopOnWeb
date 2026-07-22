using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that an eShopOnWeb user was successfully enrolled in a plan (UC1, step 6).
/// </summary>
/// <remarks>
/// Published in-process through MediatR after the provider call has already succeeded. Delivery
/// is best-effort: a failing handler is logged and never rolls back the enrollment.
/// </remarks>
public class SubscriptionActivated : INotification
{
    public SubscriptionActivated(string userName, Subscription subscription)
    {
        UserName = userName;
        Subscription = subscription;
    }

    /// <summary>The eShopOnWeb user the subscription belongs to.</summary>
    public string UserName { get; }

    public Subscription Subscription { get; }
}
