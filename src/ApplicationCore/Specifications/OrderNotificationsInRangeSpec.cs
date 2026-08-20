using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsInRangeSpec : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(notification => notification.CreatedAt >= from && notification.CreatedAt <= to);
    }
}
