using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class AllOrderPaymentsWithRefundsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
