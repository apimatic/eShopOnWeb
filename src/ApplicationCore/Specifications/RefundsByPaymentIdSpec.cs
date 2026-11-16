using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class RefundsByPaymentIdSpec : Specification<Refund>
{
    public RefundsByPaymentIdSpec(int orderPaymentId)
    {
        Query.Where(r => r.OrderPaymentId == orderPaymentId)
            .OrderBy(r => r.CreatedAt);
    }
}

public class RefundsByPaymentIdsSpec : Specification<Refund>
{
    public RefundsByPaymentIdsSpec(int[] orderPaymentIds)
    {
        var ids = orderPaymentIds.Distinct().ToArray();
        Query.Where(r => ids.Contains(r.OrderPaymentId))
            .OrderBy(r => r.CreatedAt);
    }
}
