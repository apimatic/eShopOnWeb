using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Provider-queued (scheduled, not yet sent) notifications addressed to a specific
/// contact number - used when a number is removed so nothing queued reaches it again.
/// </summary>
public class PendingNotificationsToNumberSpecification : Specification<OrderNotification>
{
    public PendingNotificationsToNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.RecipientNumber == phoneNumber
            && n.Status == NotificationStatuses.Scheduled);
    }
}
