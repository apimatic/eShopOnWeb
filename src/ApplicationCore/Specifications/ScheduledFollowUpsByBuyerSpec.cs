using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpsByBuyerSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderStatus == "scheduled");
    }
}
