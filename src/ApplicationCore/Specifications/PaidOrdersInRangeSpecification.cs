using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaidOrdersInRangeSpecification : Specification<Order>
{
    public PaidOrdersInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to && o.PayPalOrderId != null)
            .Include(o => o.Refunds);
    }
}
