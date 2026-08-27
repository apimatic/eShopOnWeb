using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up notifications for an order that are still scheduled with the provider
/// (i.e. have a provider message SID and have not reached a terminal state locally).
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == OrderNotificationType.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && n.Status == OrderNotificationStatuses.Scheduled);
    }
}
