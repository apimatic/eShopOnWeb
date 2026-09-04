using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersInDateRangeSpec : Specification<Order>
{
    public OrdersInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}