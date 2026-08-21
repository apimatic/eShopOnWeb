using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class OrdersInDateRangeSpecification : Specification<Order>
{
    public OrdersInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.Refunds);
    }
}

public class OrdersWithPayPalIdentifiersSpecification : Specification<Order>
{
    public OrdersWithPayPalIdentifiersSpecification()
    {
        Query.Where(o =>
                o.PayPalOrderId != null
                || o.AuthorizationId != null
                || o.CaptureId != null)
            .Include(o => o.Refunds);
    }
}
