using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByContactSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByContactSpecification(int contactNumberId)
    {
        Query.Where(n =>
            n.ContactNumberId == contactNumberId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderStatus == "scheduled" &&
            n.ProviderMessageSid != null);
    }
}
