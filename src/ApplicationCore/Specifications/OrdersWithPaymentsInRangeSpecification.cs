using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersWithPaymentsInRangeSpecification : Specification<Order>
{
    public OrdersWithPaymentsInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds)
            .Where(o =>
                o.Payment != null &&
                (
                    (o.Payment.AuthorizedAt != null && o.Payment.AuthorizedAt >= from && o.Payment.AuthorizedAt <= to) ||
                    (o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to) ||
                    (o.OrderDate >= from && o.OrderDate <= to)
                ));
    }
}
