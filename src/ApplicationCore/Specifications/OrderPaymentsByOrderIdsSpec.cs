using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpec(int[] orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
