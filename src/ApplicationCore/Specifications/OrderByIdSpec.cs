using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdSpec : Specification<Order>
{
    public OrderByIdSpec(int id)
    {
        Query.Where(o => o.Id == id)
             .Include(o => o.OrderItems);
    }
}

public class OrderByIdAndBuyerSpec : Specification<Order>
{
    public OrderByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(o => o.Id == id && o.BuyerId == buyerId)
             .Include(o => o.OrderItems);
    }
}

public class OrdersByDateRangeSpec : Specification<Order>
{
    public OrdersByDateRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(o => o.OrderDate >= from && o.OrderDate <= to)
             .Include(o => o.OrderItems);
    }
}
