using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A shopper's messages to a specific number that are still queued for a future send. Used when a
/// number is removed, so nothing already queued can still be sent to it.
/// </summary>
public class ScheduledNotificationsForBuyerNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsForBuyerNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.ToPhoneNumber == phoneNumber
            && n.Status == MessageDeliveryStatus.Scheduled);
    }
}
