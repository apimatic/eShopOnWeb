using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingNotificationsByContactSpec : Specification<OrderNotification>
{
    public PendingNotificationsByContactSpec(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
                         && n.ProviderMessageSid != null
                         && (n.ProviderStatus == "scheduled"
                             || n.ProviderStatus == "queued"
                             || n.ProviderStatus == "accepted"));
    }
}
