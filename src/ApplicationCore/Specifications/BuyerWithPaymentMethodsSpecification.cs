using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A buyer (by identity) with their saved cards, for shopper-scoped payment-method access.</summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identityGuid)
    {
        Query
            .Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
