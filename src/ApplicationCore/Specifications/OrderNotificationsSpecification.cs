using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications sent for a given order.</summary>
public class OrderNotificationsSpecification : Specification<SmsNotification>
{
    public OrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId);
    }
}
