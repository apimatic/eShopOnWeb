using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that were accepted by the provider and may
/// still be scheduled there (candidates for cancellation when the order is cancelled).
/// </summary>
public class PendingFollowUpsSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.MessageSid != null
            && n.Status != NotificationStatuses.Failed);
    }
}
