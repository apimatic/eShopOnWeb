using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, oldest first.</summary>
public sealed class SmsNotificationsByOrderSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.Id);
    }
}
