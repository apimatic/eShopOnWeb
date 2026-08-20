using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaidOrdersSpecification : Specification<Order>
{
    public PaidOrdersSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null && o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.PaymentRefunds)
            .Include(o => o.OrderItems);
    }
}

public class AllPaidOrdersSpecification : Specification<Order>
{
    public AllPaidOrdersSpecification()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.PaymentRefunds)
            .Include(o => o.OrderItems);
    }
}
