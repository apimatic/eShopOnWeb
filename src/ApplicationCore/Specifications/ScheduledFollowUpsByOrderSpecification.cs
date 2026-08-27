using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that are still queued with the
/// provider (status "scheduled") and have not yet gone out.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == OrderNotificationType.DeliveryFollowUp
            && n.Status == "scheduled");
    }
}
