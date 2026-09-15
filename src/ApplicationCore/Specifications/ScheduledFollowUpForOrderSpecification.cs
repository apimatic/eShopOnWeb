using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-up messages queued with the provider for an order that have not yet been
/// cancelled — the ones a cancellation must call off before they go out.
/// </summary>
public class ScheduledFollowUpForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                      && n.Kind == NotificationKind.DeliveryFollowUp
                      && n.IsScheduled
                      && n.ProviderMessageSid != null
                      && n.Status != "canceled");
    }
}
