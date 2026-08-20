using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using System;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsCreatedInRangeSpec : Specification<OrderNotification>
{
    public OrderNotificationsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
