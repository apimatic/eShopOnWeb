using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentInDateRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Refunds)
            .OrderBy(o => o.OrderDate);
    }
}
