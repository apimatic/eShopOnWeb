using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every payment with its refunds — used by operator reconciliation to line eShop's
/// own record up against PayPal's.</summary>
public class AllOrderPaymentsSpecification : Specification<OrderPayment>
{
    public AllOrderPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
