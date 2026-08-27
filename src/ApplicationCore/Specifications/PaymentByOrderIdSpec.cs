using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The most recent payment attempt for an order, including its refunds.
/// </summary>
public class PaymentByOrderIdSpec : Specification<Payment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query
            .Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds)
            .OrderByDescending(p => p.Id);
    }
}
