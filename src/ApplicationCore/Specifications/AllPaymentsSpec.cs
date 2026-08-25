using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class AllPaymentsSpec : Specification<Payment>
{
    public AllPaymentsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
