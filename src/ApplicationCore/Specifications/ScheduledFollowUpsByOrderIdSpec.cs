using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "pending"));
    }
}
