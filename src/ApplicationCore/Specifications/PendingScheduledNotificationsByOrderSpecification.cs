using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Scheduled messages for an order that the provider has accepted but not yet sent.</summary>
public class PendingScheduledNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.IsScheduled && n.ProviderMessageSid != null);
    }
}
