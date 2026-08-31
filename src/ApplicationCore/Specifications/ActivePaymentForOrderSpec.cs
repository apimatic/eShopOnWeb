using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The latest non-failed payment attempt for an order, including refunds.
/// </summary>
public class ActivePaymentForOrderSpec : Specification<Payment>
{
    public ActivePaymentForOrderSpec(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId && p.Status != Payment.Statuses.Failed)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.Id);
    }
}
