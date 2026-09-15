using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads a buyer with their saved cards, by identity.</summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identity)
    {
        Query
            .Where(b => b.IdentityGuid == identity)
            .Include(b => b.PaymentMethods);
    }
}
