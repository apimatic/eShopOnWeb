using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads every order whose payment has been captured, with its refunds — the eShop side of a
/// reconciliation against PayPal's transaction records.
/// </summary>
public class CapturedOrdersSpecification : Specification<Order>
{
    public CapturedOrdersSpecification()
    {
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Where(o => o.Payment != null && o.Payment.CaptureId != null);
    }
}
