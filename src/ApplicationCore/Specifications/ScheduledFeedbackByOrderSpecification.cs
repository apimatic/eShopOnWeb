using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The delivery-feedback follow-ups queued for an order, used to call them off when the
/// order is cancelled.</summary>
public sealed class ScheduledFeedbackByOrderSpecification : Specification<Notification>
{
    public ScheduledFeedbackByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Type == NotificationType.DeliveryFeedback);
    }
}
