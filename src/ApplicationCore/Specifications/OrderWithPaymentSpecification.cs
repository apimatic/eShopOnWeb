using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentSpecification : Specification<Order>
{
    public OrderWithPaymentSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
            .ThenInclude(p => p.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
            .ThenInclude(p => p.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}

public class OrdersPaidInRangeSpecification : Specification<Order>
{
    public OrdersPaidInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Payment != null)
            .Include(o => o.Payment!)
            .ThenInclude(p => p.Refunds);
    }
}
