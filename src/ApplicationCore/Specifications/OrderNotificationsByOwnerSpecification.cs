using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications belonging to one shopper (across all their orders).</summary>
public class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId)
             .OrderBy(n => n.CreatedDate)
             .ThenBy(n => n.Id);
    }
}
