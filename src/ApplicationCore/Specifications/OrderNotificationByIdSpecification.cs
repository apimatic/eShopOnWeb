using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single notification by id, loaded via a tracking query.</summary>
public class OrderNotificationByIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}
