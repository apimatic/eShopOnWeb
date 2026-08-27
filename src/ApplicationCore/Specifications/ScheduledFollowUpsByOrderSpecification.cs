using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Delivery follow-ups for an order that the provider is still holding for later delivery.</summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query
            .Where(n => n.OrderId == orderId
                && n.Type == OrderNotificationType.DeliveryFollowUp
                && n.Status == "scheduled"
                && n.ProviderMessageSid != null);
    }
}
