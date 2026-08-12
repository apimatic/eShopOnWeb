using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that the provider still holds as scheduled (not yet sent), so
/// they can be called off if the order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.Status == "scheduled" &&
            n.ProviderMessageSid != null);
    }
}
