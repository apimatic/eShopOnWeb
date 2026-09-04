using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderRefundByIdempotencyKeySpec : Specification<OrderRefund>
{
    public OrderRefundByIdempotencyKeySpec(int orderId, string idempotencyKey)
    {
        Query.Where(r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey);
    }
}