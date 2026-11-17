using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsWithRefundsSpec : Specification<Payment>
{
    public PaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
