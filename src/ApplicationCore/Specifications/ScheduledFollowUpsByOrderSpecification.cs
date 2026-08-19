using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled follow-up messages for an order that are still pending with the
/// provider (i.e. eligible to be called off when the order is cancelled).
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.Status == NotificationStatus.Scheduled);
    }
}
