using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items and (owned) payment/refund state. The owned Payment and its
/// refunds are materialised automatically by EF as part of the aggregate root.
/// </summary>
public sealed class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
