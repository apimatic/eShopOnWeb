using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersForReconciliationSpecification : Specification<Order>
{
    public OrdersForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(order => order.OrderDate >= from && order.OrderDate <= to)
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .Include(order => order.Refunds)
            .OrderBy(order => order.OrderDate);
    }
}
