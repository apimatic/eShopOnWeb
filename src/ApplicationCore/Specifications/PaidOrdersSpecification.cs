using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaidOrdersSpecification : Specification<Order>
{
    public PaidOrdersSpecification()
    {
        Query.Where(o => o.PayPalOrderId != null || o.PayPalAuthorizationId != null || o.PayPalCaptureId != null)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}
