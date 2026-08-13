using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SmsNotificationsByBuyerSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedDate);
    }

    public SmsNotificationsByBuyerSpecification(string buyerId, IEnumerable<int> orderIds)
    {
        Query.Where(n => n.BuyerId == buyerId && orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedDate);
    }
}
