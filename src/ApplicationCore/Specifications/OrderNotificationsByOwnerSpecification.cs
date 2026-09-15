using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification produced for a single shopper's orders.</summary>
public class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId);
    }
}
