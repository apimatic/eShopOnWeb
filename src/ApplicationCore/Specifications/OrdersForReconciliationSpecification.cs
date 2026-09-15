using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersForReconciliationSpecification : Specification<Order>
{
    public OrdersForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.Payment != null && o.Payment.PayPalOrderId != null))
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
