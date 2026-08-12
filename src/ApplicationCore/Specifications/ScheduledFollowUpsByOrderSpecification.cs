using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled follow-up messages for an order that are still queued with the provider (have a
/// message id and have not already been cancelled) — the ones to call off when the order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && n.MessageSid != null
            && n.Status != NotificationStatuses.Canceled);
    }
}
