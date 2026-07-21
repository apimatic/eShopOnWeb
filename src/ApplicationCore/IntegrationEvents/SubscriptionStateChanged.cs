using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after a lifecycle transition — pause/resume/cancel/reactivate (UC4).</summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference, int subscriptionId, SubscriptionStatus oldStatus, SubscriptionStatus newStatus)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public string UserReference { get; }
    public int SubscriptionId { get; }
    public SubscriptionStatus OldStatus { get; }
    public SubscriptionStatus NewStatus { get; }
}
