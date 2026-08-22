using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByContactNumberIdSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactNumberIdSpec(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "pending"));
    }
}
