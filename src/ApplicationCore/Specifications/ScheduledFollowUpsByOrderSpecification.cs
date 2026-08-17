using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that are still scheduled (not yet sent) and have a provider id —
/// exactly the messages that must be called off when an order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<SmsNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.Status == NotificationStatus.Scheduled
            && n.ProviderMessageId != null);
    }
}
