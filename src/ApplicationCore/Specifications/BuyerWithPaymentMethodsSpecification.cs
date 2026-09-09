using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a shopper's buyer record together with their saved cards, keyed by the shopper's identity
/// (the username carried on the JWT). Scopes saved cards to their owner.
/// </summary>
public class BuyerWithPaymentMethodsSpecification : Specification<Buyer>
{
    public BuyerWithPaymentMethodsSpecification(string identity)
    {
        Query
            .Where(b => b.IdentityGuid == identity)
            .Include(b => b.PaymentMethods);
    }
}
