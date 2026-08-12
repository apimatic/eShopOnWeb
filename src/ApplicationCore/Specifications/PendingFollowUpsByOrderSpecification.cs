using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up notifications for an order that are still queued with the provider and have not gone out
/// yet — the ones that must be called off when the order is cancelled.
/// </summary>
public sealed class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsFollowUp
                         && n.ProviderMessageId != null
                         && (n.Status == NotificationDeliveryStatus.Scheduled
                             || n.Status == NotificationDeliveryStatus.Accepted
                             || n.Status == NotificationDeliveryStatus.Queued));
    }
}
