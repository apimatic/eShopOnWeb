using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByDateRangeSpecification : Specification<Order>
{
    public OrdersByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(order => order.OrderDate >= from && order.OrderDate <= to)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
