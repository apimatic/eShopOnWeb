using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-ups for an order that are still scheduled with the provider and have not yet
/// been cancelled — the messages that must be called off when the order is cancelled.
/// </summary>
public class PendingFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.IsScheduled
            && !n.ScheduleCancelled
            && n.ProviderMessageSid != null);
    }
}
