using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up messages for an order that are still queued with the provider
/// (and can therefore still be called off).
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.ProviderStatus == "scheduled"
            && n.ProviderMessageSid != null);
    }
}
