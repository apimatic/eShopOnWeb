using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class AllPaymentsWithRefundsSpec : Specification<Payment>
{
    public AllPaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
