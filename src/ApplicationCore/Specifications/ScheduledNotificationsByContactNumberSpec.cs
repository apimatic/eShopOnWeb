using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledNotificationsByContactNumberSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactNumberSpec(int contactNumberId)
    {
        Query.Where(n =>
            n.ContactNumberId == contactNumberId &&
            n.ProviderMessageSid != null &&
            (n.ProviderStatus == "scheduled" || n.ProviderStatus == "queued" || n.ProviderStatus == "accepted" || n.ProviderStatus == "pending"));
    }
}
