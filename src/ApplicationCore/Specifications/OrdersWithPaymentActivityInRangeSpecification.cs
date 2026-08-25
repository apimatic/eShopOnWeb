using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Orders whose capture or any refund happened within the given range — the eShop side of a reconciliation report.</summary>
public class OrdersWithPaymentActivityInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentActivityInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query
            .Where(o => o.Payment != null &&
                ((o.Payment.CapturedOn != null && o.Payment.CapturedOn >= from && o.Payment.CapturedOn <= to) ||
                 o.Payment.Refunds.Any(r => r.CreatedOn >= from && r.CreatedOn <= to)))
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
