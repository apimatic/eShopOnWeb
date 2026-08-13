using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled follow-up notifications for an order that are still queued at the provider —
/// i.e. not yet sent and not already cancelled — so they can be called off when the order is cancelled.
/// </summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduled
                         && n.ProviderMessageSid != null
                         && n.Status == NotificationStatus.Scheduled);
    }
}
