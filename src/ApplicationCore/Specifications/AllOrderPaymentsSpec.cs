using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class AllOrderPaymentsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
