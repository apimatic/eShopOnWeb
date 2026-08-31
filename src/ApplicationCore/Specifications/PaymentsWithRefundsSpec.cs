using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsWithRefundsSpec : Specification<Payment>
{
    public PaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
