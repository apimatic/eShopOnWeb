using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A buyer with their saved cards, scoped by identity so one shopper never sees another's.
/// </summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification
{
    public BuyerWithPaymentMethodsSpecification(string identityGuid)
    {
        Query
            .Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
