using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds, used to line eShop records up against PayPal's ledger.</summary>
public sealed class AllPaymentsWithRefundsSpec : Specification<Payment>
{
    public AllPaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
