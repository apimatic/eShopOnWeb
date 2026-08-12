using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Not-yet-sent scheduled follow-ups for an order — the ones a cancellation must call off so the
/// "how did delivery go?" message never reaches a customer whose order was cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && n.ProviderMessageSid != null
            && n.Status == "scheduled");
    }
}
