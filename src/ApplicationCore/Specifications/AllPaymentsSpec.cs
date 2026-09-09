using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads every payment (with refunds), used by reconciliation to line up both ledgers.</summary>
public class AllPaymentsSpec : Specification<Payment>
{
    public AllPaymentsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
