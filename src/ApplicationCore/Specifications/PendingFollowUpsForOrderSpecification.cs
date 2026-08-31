using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that are still queued with the provider
/// (scheduled but not yet sent) and can therefore still be called off.
/// </summary>
public class PendingFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.Status == NotificationStatuses.Scheduled);
    }
}
