using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null);
    }
}
