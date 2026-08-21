using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Orders whose payment has been captured (has a PayPal capture id) — the eShop side of reconciliation.</summary>
public class CapturedOrdersSpec : Specification<Order>
{
    public CapturedOrdersSpec()
    {
        Query
            .Where(order => order.Payment != null && order.Payment.CaptureId != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
