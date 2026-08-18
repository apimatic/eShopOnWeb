using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled follow-up message(s) for an order that are still queued at the provider (have an id and
/// are still in the <c>scheduled</c> state) and so can still be called off before they go out.
/// </summary>
public class PendingFollowUpByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduledFollowUp
            && n.MessageSid != null
            && n.Status == MessageDeliveryStatuses.Scheduled);
    }
}
