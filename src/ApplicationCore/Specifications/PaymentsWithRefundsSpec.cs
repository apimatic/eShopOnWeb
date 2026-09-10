using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds loaded (used for reconciliation).</summary>
public sealed class PaymentsWithRefundsSpec : Specification<Payment>
{
    public PaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
