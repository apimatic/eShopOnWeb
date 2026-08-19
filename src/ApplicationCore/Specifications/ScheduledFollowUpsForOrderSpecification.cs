using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up messages for one order that are still scheduled with the provider (so they can be
/// called off if the order is cancelled).
/// </summary>
public class ScheduledFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.Status == MessageStatuses.Scheduled
            && n.ProviderMessageSid != null);
    }
}
