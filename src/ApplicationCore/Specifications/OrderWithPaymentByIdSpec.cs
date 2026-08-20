using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}

public class OrdersWithPaymentInRangeSpec : Specification<Order>
{
    public OrdersWithPaymentInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query
            .Where(o => o.OrderDate >= from && o.OrderDate <= to
                        || (o.Payment.AuthorizedAt != null && o.Payment.AuthorizedAt >= from && o.Payment.AuthorizedAt <= to)
                        || (o.Payment.CapturedAt != null && o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}

public class OrdersWithPayPalReferencesSpec : Specification<Order>
{
    public OrdersWithPayPalReferencesSpec()
    {
        Query
            .Where(o => o.Payment.PayPalOrderId != null
                        || o.Payment.AuthorizationId != null
                        || o.Payment.CaptureId != null)
            .Include(o => o.Refunds);
    }
}
