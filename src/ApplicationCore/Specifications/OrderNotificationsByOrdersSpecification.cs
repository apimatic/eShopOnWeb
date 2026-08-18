using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications for a set of orders — used to show each of a shopper's orders with its notification state.</summary>
public sealed class OrderNotificationsByOrdersSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId));
    }
}
