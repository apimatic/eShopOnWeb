using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class PaymentsByOrderIdsSpecification : Specification<Payment>
{
    public PaymentsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
