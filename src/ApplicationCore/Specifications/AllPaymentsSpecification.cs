using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All payments with their refunds. Used by reconciliation to gather every PayPal transaction id
/// eShop knows about (captures and refunds) to line up against PayPal's own record.
/// </summary>
public class AllPaymentsSpecification : Specification<Payment>
{
    public AllPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
