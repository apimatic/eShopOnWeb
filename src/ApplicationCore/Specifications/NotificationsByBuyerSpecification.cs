using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications addressed to one shopper.</summary>
public class NotificationsByBuyerSpecification : Specification<Notification>
{
    public NotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
             .OrderBy(n => n.CreatedDate);
    }
}
