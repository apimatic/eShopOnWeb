using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders that have any PayPal payment state attached, for reconciliation against
/// PayPal's own transaction records.
/// </summary>
public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query.Where(o => o.PayPalOrderId != null)
            .Include(o => o.PaymentRefunds);
    }
}
