using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Include(o => o.OrderItems)
            .Include(o => o.Refunds);
    }
}
