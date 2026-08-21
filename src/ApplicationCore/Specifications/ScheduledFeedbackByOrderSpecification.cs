using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFeedbackByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFeedbackByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFeedback
                         && n.ProviderMessageSid != null);
    }
}
